# SGT Production Box

FF14 的**生产清单规划插件**（Dalamud / 卫月）。给一个目标物品和数量，它会展开整棵配方树，告诉你每个材料是**要制作、要采集、要兑换还是 NPC 购买**，并可以一键把制作任务交给 Artisan。

> 目标环境：Dalamud **API 15**（7.5 版本线）、.NET 10 SDK。

## 功能

- **多清单**：每个清单独立命名、独立保存，随插件配置持久化。
- **配方树**：递归展开到基础材料，自动处理"一次制作产出多个"的数量换算（例如做 4 个、每次产 3 个 → 按 2 次制作计算）。
- **材料分类**（按优先级判断）：
  1. `[制作]` 有配方 —— 半成品/成品，可以继续展开或交给 Artisan；
  2. `[采集]` 在采集表里 —— 采矿/园艺/捕鱼材料；
  3. `[兑换]` 特殊商店 —— 显示"兑换：诗学神典石 ×N"这类信息；
  4. `[购买]` 金币商店 —— NPC 直接买；
  5. `[未知]` 没有识别出来源。
- **材料汇总**：把树上的叶子按物品合并，统计总需求，一键复制到剪贴板。
- **Artisan 联动**：点"制作"调用 `Artisan.CraftItem`，把配方和数量直接交给 Artisan 排队制作。

## 使用

1. 游戏内输入 `/sgt`（或 `/sgtbox`）打开主窗口。
2. 左侧顶部是清单下拉框 + 「新建 / 删除」；下面每个清单是一个**可折叠分组**：点标题展开/收起物品列表，点其他清单的标题会把它切换成当前清单（当前清单标题显示为蓝色），右侧材料总览跟着切。
3. 搜索物品名（支持中文/英文/物品 ID），点搜索结果加入**当前清单**。
4. 清单每行：**物品名** → **数量输入框**（可以直接打字）→ `-` / `+` 微调；点名字选中，右键删除。
5. 右侧界面：
   - **顶部三栏**（三栏高度一致）：左边是**配方树**（点击卡片类型标签后显示，可关闭），中间是**完成度圆环** + 标题 + 汇总 + 「刷新背包 / 复制清单 / 设置」按钮，右边一栏上半是「制作」清单（需求数量 + 制作次数）、下半是「采集」清单（还需采集数量，够了显示"已够"）；
   - **筛选标签**：全部 / 仅缺料 / 制作 / 采集 / 兑换 / 购买，点一下只显示对应材料；
   - **材料卡片**：按类型分组，卡片上有图标、名称、类型标签和**背包/手工**标签、`现有 / 需求` 进度条、还需数量和制作次数；
   - **背包 / 手工**：默认「背包」= 按背包现有数量抵扣；点这个标签切换成「手工」= 忽略背包，这个材料按需求量全部重新采集/准备（现有记 0、还需 = 需求）；再点切回。筛选标签右边有「全背包 / 全手工」一键切换；设置里也可以「全部恢复使用背包」；
   - **库存抵扣**：配方树会把背包里已有的成品、半成品按层扣掉——比如要 100 个成品、背包里有 30 个，就只按 70 个展开材料；切到「手工」则不扣这 30 个，采集需求会跟着变大；
   - **配方树**：默认隐藏，**点击卡片上的类型标签（制作/采集/兑换/购买）**才会在标题下方展开该材料的配方树，点「关闭」收起；
   - 材料数量是整个清单合并后的总量，**现有**来自背包（4 格 + 陆行鸟鞍囊 + 水晶），每秒自动刷新，也可以点「刷新背包」；
   - 配方树节点：每个材料显示本分支数量，同一材料在多个分支出现时会标注 `（总 N）`，鼠标悬停能看到 `需要 / 已有 / 还需`；节点上的「制作」按钮按总量交给 Artisan。
6. 点「复制」可以把整张材料表（含已有数量）复制到剪贴板。

### 命令

| 命令 | 作用 |
| --- | --- |
| `/sgt`、`/sgtbox` | 打开 / 关闭主窗口 |
| `/sgt config`、`/sgtbox 设置` | 打开设置 |
| `/sgt add <物品名>` | 搜索并直接加入当前清单，例如 `/sgt add 恢复药` |

## 联动说明（重要）

- **Artisan（制作）**：已接入，IPC 为 `Artisan.CraftItem(recipeId, amount)`（源码里是 `ushort recipeId, int quantity`）。没装 Artisan 时点"制作"会在聊天栏提示。
- **采集**：GatherBuddy / GatherBuddyReborn 目前**没有**"采集指定物品"的公开 IPC（我读了它们的 `GatherBuddyIpc.cs`，只有 `Identify`、`Version`、自动采集开关、鱼时间等），所以本插件只能把材料标记为 `[采集]`，不能替你自动去采。以后它们加了接口，或者在用别的有 IPC 的采集插件，可以再接。

## 自动流程（采集 → 回房屋 → 制作）

`/sgt config` → 设置里：

0. 顶部「必备插件」会检测 **Artisan / Lifestream / GatherBuddyReborn** 是否已安装（绿点已安装、红点未检测到）；缺 Artisan 或需回房屋却缺 Lifestream 时，开始自动流程会直接提示。
1. 「返回地点」选择：**不返回（原地制作） / 个人房屋 / 部队房屋**
2. 点「开始自动流程」

流程：

- **等待采集**：每 0.5 秒看一次背包，直到清单里所有 `[采集]` 材料都够数。这就是"GBR 采集完成"的判定（不看 GBR 的状态文本，避免中英文/版本差异）；等待期间状态里会顺带显示 GBR 的自动采集状态。
- 材料够了会**自动关掉 GBR 自动采集**，避免它继续跑和传送打架。
- **回房屋**：调用 Lifestream 的 `Lifestream.TeleportToHome` / `Lifestream.TeleportToFC`，然后等 Lifestream 不忙。选「不返回」则跳过这一步。
- **制作**：按配方依赖从深到浅排序，逐项调用 `Artisan.CraftItem(recipeId, amount)`；背包里已经够数的物品会跳过；每项等 Artisan 空闲后再提交下一项。
- 任一环节失败会在聊天栏报错，设置窗口里也会显示当前状态。

依赖：**Lifestream**（回房屋）、**Artisan**（制作）、**GatherBuddyReborn**（采集）。没有 Lifestream 时选择回房屋会直接失败并提示。

## 已知限制

- 兑换只取商店里第一条成本的货币和数量，没有处理多货币组合、票据上限、周限。
- 同一个物品有多张配方时，取**配方等级最低**的那张（通常是最容易做的）。
- 没有做 HQ 材料、雇员/部队储物柜库存（雇员背包只有打开雇员窗口时才可读），现有数量只统计玩家背包和陆行鸟鞍囊。
- 采集没有做"具体地图/时段/采集点"信息（需要 GatheringPoint 相关表，后续可以加）。
- 自动流程还没在游戏里长时间跑过：Artisan 的排队语义、Lifestream 的"传送完成"判定可能随版本有差异，建议先用小清单试跑。

## 编译

编译需要 **.NET 10 SDK**，以及一份本地 Dalamud 程序集（装好 XIVLauncher 并让 Dalamud 更新一次后，位于 `%APPDATA%\XIVLauncher\addon\Hooks\dev`；国服启动器的目录是 `XIVLauncherCN`，用 `DALAMUD_HOME` 指过去即可）。

```powershell
$env:DALAMUD_HOME = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev"
dotnet build -c Release
```

产物在 `SGTProductionBox/bin/Release/`：`SGTProductionBox.dll`、`SGTProductionBox.json`（清单）和依赖文件。

## 装进游戏

1. 启动 XIVLauncher（国服 XIVLauncherCN）并进入游戏。
2. `/xlsettings` → Experimental → **Dev Plugin Locations** 添加 `SGTProductionBox.dll` 的完整路径。
3. `/xlplugins` → Dev Tools → Installed Dev Plugins → 启用「SGT Production Box」。
4. `/sgt` 打开。

## 发布给其他人用

推荐做法：**GitHub 仓库 + Release + 自建插件仓库（repo.json）**，玩家在 `/xlsettings` 里加一次仓库地址就能搜到并安装，后续你发新版本他们能直接更新。

### 1. 准备仓库

1. 建一个 public GitHub 仓库，把 `SGTProductionBox` 整个目录推上去（`bin/`、`obj/` 已被 `.gitignore` 排除）。
2. 改 `SGTProductionBox.csproj` 里的 `Author`、`RepoUrl`、`Tags` 成你自己的信息；`Name` 就是插件列表里显示的名字（现在是 `SGT Production Box`）。
3. 每次发版把 `<Version>` 加一（例如 `1.0.0.1`），并打一个 tag。

### 2. 打包 + 生成 repo.json

仓库里已经带了 `publish.ps1`，一条命令完成编译、打包和 repo.json 更新：

```powershell
.\publish.ps1 -Owner <你的GitHub用户名> -Repo SGTProductionBox
```

它会：

1. 用 `DALAMUD_HOME`（自动找 `XIVLauncher` / `XIVLauncherCN` 的 dev 目录）编译 Release
2. 把 DalamudPackager 生成的 `bin\Release\SGTProductionBox\latest.zip` 复制成 `dist\SGTProductionBox-v<版本>.zip`
3. 按当前版本、你的 GitHub 仓库地址重写 `repo.json`（`DownloadLink`、`LastUpdate`、`AssemblyVersion` 自动填好）

然后把这个 zip 上传到 GitHub Release（tag 用 `v<版本>`，例如 `v1.0.0.0`），提交 `repo.json` 即可。

也可以直接打一个 `v*` tag，让 `.github/workflows/release.yml` 在 GitHub Actions 里完成全流程：编译 → 上传 Release zip → 用 `update-repo.ps1` 重写 `repo.json` → 自动提交回默认分支（CI 会自己下载 Dalamud dev 文件）。

### 3. repo.json 说明

`repo.json` 是给 Dalamud 的插件仓库清单，`publish.ps1` 会自动生成它，格式如下：

```json
[
  {
    "Author": "SGT",
    "Name": "SGT Production Box",
    "InternalName": "SGTProductionBox",
    "AssemblyVersion": "1.0.0.0",
    "Description": "多清单生产规划：配方树、材料分类、一键 Artisan 制作。",
    "ApplicableVersion": "any",
    "DalamudApiLevel": 15,
    "Tags": ["crafting", "recipe", "production", "artisan"],
    "CategoryTags": ["utility"],
    "LoadPriority": 0,
    "IsHide": false,
    "IsTestingExclusive": false,
    "DownloadLink": "https://github.com/<你的名字>/<仓库名>/releases/download/v1.0.0.0/SGTProductionBox-v1.0.0.0.zip",
    "LastUpdate": 1760000000
  }
]
```

- `DownloadLink` 必须是 zip 直链
- `LastUpdate` 是 Unix 时间戳（秒）
- 改版本时 repo.json 里的 `AssemblyVersion`、`DownloadLink`、`LastUpdate` 要同步改
- 如果仓库开了 GitHub Pages，可以用 `https://<你>.github.io/<仓库名>/repo.json`；否则用 raw 链接

### 4. 玩家怎么装

国内直连（不用代理，推荐给国内朋友）：

```
https://ghproxy.net/https://raw.githubusercontent.com/SlunyuGuiT-L/SGTProductionBox/main/repo.json
```

有代理 / 国外：

```
https://raw.githubusercontent.com/SlunyuGuiT-L/SGTProductionBox/main/repo.json
```

`/xlsettings` → **Custom Plugin Repositories** → 填上面任意一个 → 勾选 + 保存 → `/xlplugins` 的 **Available Plugins** 里搜索 `SGT Production Box` 安装。

> 说明：GitHub 的 release 下载在国内经常连不上，所以 `repo.json` 里的 `DownloadLink` 默认走 `ghproxy.net` 镜像；镜像失效时可以换成 `gh-proxy.com` 再重新生成 `repo.json`（`ghfast.top` 在部分代理环境下 .NET TLS 握手会失败）。

### 其他方式

- **直接发 zip**：对方解压后用 Dev Plugin Locations 加载（需要开发者模式），适合小范围测试。
- **提交官方仓库**：按 <https://dalamud.dev/plugin-publishing/submission> 的流程 PR；需要开源许可证，并且要遵守 [AI 使用政策](https://dalamud.dev/plugin-publishing/ai-policy)——完全由 AI 生成、没有自己实际修改和测试的插件会被拒绝，提交时要如实披露 AI 参与程度。

发布前建议写清楚：这个插件会调用 **Artisan / Lifestream / GatherBuddyReborn**，以及自动流程会做传送和制作，提醒用户先用小清单试。

## 目录结构

```
SGTProductionBox/
├─ SGTProductionBox.csproj     # 项目 + 插件清单
├─ Plugin.cs            # 入口：服务、窗口、命令
├─ Configuration.cs     # 多清单与设置持久化
├─ Crafting/
│  ├─ CraftModels.cs    # 清单 / 节点 / 分类模型
│  └─ CraftData.cs      # 配方、采集、商店索引与配方树构建
├─ Services/
│  └─ ArtisanIpc.cs     # Artisan IPC 封装
└─ Windows/
   ├─ MainWindow.cs     # 清单 + 配方树 + 汇总
   └─ ConfigWindow.cs   # 设置
```

## 排查

- **插件列表里看不到**：确认 Dev Plugin Locations 填的是 dll 完整路径，且同目录有 `SGTProductionBox.json`；重新编译后禁用再启用插件。
- **搜索很慢**：第一次搜索要建立物品索引（约 4 万条），之后走缓存；配方和商店索引在第一次展开配方树时建立。
- **点制作提示 Artisan 不可用**：先装 Artisan 并确认它已加载。
