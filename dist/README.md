# ComfyShellExt

Windows 11 资源管理器（Explorer）缩略图扩展：检测图片 / 视频里嵌入的 AI 生图元数据
（ComfyUI 工作流，以及 SD WebUI / A1111、NovelAI、SwarmUI、Fooocus、InvokeAI 的生成
参数），有则按生成工具在**缩略图右上角**叠加缩写角标（`Comfy`、`A1111`、`NAI`……）。
附带的命令行工具可以把 ComfyUI 工作流 JSON 提取成一个可搜索的本地数据库。

结构参照 `D:\Green\apkshellext2`：绿色单目录、`regasm /codebase` 注册、
`install.bat` / `uninstall.bat` / `restart_explorer.bat`。

## 三步上手

1. 把 `dist` 整个目录放到一个**以后不会移动**的位置（例如 `D:\Green\ComfyShellExt`）。
   注册表会记录当前路径，移动目录后扩展就失效了。
2. 右键 `install.bat` → 以管理员身份运行。安装脚本会打印每个扩展名被谁接管、
   原来的处理器是谁，最后可以选择立即重启资源管理器。
3. 已经看过的缩略图仍是旧的缓存图，运行一次 `clear_thumbnail_cache.bat` 即可。

卸载：右键 `uninstall.bat` → 以管理员身份运行。安装时备份的原始缩略图处理器会被逐项恢复。

## 目录内容

| 文件 | 说明 |
| --- | --- |
| `ComfyShellExt.dll` | shell 扩展本体（图片 / 视频缩略图处理器 + 右键菜单处理器） |
| `ComfyWorkflowMenu.exe` | 右键菜单命令的执行程序：查看 / 导出（无控制台窗口） |
| `ComfyWorkflowDb.exe` | 命令行工具：扫描、建库、搜索、导出、预览、诊断 |
| `ComfyShellExt.ini` | 配置：处理哪些扩展名、角标样式、右键菜单、ffmpeg 路径、日志 |
| `install.bat` | 注册（自动请求管理员权限） |
| `uninstall.bat` | 反注册并恢复原处理器 |
| `restart_explorer.bat` | 重启 explorer.exe |
| `clear_thumbnail_cache.bat` | 清理 Windows 缩略图缓存（只删可重建的 `thumbcache_*.db`） |

## 右键菜单：查看 AI 生图信息

检测到 AI 生图元数据的文件，右键会多出一项：**查看 AI 生图信息**。
支持 ComfyUI、SD WebUI / A1111（含 Forge 等分支）、NovelAI、SwarmUI、Fooocus、InvokeAI；
普通照片和不含这些元数据的文件不会出现菜单项。

这一项会生成一个自带样式的 HTML 页面并用默认浏览器打开，内容包括：

- 生成工具、来源元数据块（如 `png:tEXt:parameters`）；
- 生成参数表（模型 / LoRA / 采样器 / 步数 / CFG / 种子 / 尺寸等，按各工具的字段解析；
  对 ComfyUI 的 API 格式是顺着 KSampler 的 positive、negative 连线找到的真实提示词）；
- 正负提示词；
- 用到的节点（ComfyUI）；
- workflow / prompt / 各工具设置 JSON，可一键复制或另存（这就是原来的导出功能）；
- "全部元数据"展开卡：文件里每一条文本元数据的出处和内容，未知工具也能看个明白。

多选时一个页面列出每个文件的信息；选中的文件里没有 AI 元数据的会单独列出。

`ComfyWorkflowMenu.exe` 必须和 `ComfyShellExt.dll` 放在同一个目录里，菜单命令靠它执行；
缺失时点菜单会弹窗告知，而不是静默无反应。

**Windows 11 的位置**：默认用的是"动态"处理器，它会读文件内容，只在真的含 AI 元数据时
才出现——但 Win11 的一级右键菜单不加载这类旧式扩展，所以它在 **"显示更多选项"** 里
（按 **Shift+右键** 可直接打开经典菜单）。

想让菜单项出现在 Win11 一级菜单，把 ini 改成 `staticverbs = 1` 再重跑 `install.bat`：
静态动词能进一级菜单，代价是无法读取文件内容，配置的所有图片/视频都会显示这一项
（点了之后会提示该文件有没有可识别的元数据）。两种方式可以只开一个，也可以都开。

## 右键菜单：混淆 / 解混淆图片

图片（不含视频）还会多出两项，**不需要识别到元数据**，任何配置的图片都有：

- **混淆图片（像素重排）** —— 沿 Gilbert 空间填充曲线把每个像素移动固定步长，画面变成
  纯噪点，同时输出文件剥掉全部元数据（工作流、提示词、EXIF 都不带走）。
  输出为 `<原名>_混淆.png`，一律 PNG 保证无损。
- **解混淆图片（像素重排）** —— 同一条曲线反向移动，把噪点还原成原图。
  输出为 `<原名>_还原.png`。

重排是纯像素置换，无损可逆；步长只由图片宽高决定（黄金分割比），解混淆不需要密钥文件。
要点：

- 输出一律 PNG。JPEG 源图也会转成 PNG 存（体积变大，但像素分毫不差）。
- 动图（GIF / 多帧 TIFF / 动画 WebP）只处理第一帧。
- 混淆两次就要解混淆两次；已经重新编码过的"混淆图"（例如另存成 JPEG）无法还原。
- 超大图（超过 6400 万像素）会拒绝处理并提示。
- webp / avif 走 WIC 解码，系统缺对应编解码器扩展时会提示跳过。
- 与"图片混淆"网页版工具（Gilbert 曲线 + 黄金分割步长）互相兼容：
  网页版混淆的图可用解混淆还原，反之亦然。

### 元数据去留（`obfuscate.keepmeta`）

ini 的 `[obfuscate]` 段控制混淆时原图元数据（ComfyUI 工作流 / 提示词 / EXIF）的去留：

- `keepmeta = 1`（当前设置）：元数据压缩扰码后随混淆文件一起保存——文件里看不到
  明文工作流（`check` 显示 workflow: no），但解混淆时会自动原样写回，
  输出的画面和元数据都与原图一致。
- `keepmeta = 0`：混淆文件里完全不留工作流痕迹，解混淆也无法恢复元数据
  （想保留元数据请另存原图）。

该开关只影响之后的**混淆**操作；解混淆按混淆文件里实际存的元数据决定写回与否。
改动即时生效，不用重装。注意：网页版工具混淆的图片本身不携带元数据，
无论怎么设置都恢复不出来；扰码与像素重排同为无密钥方案，能解像素的人就能解元数据。

### 混淆文件的资源管理器预览（`obfuscate.preview`）

`[obfuscate] preview = 1` 时（当前设置），混淆文件在资源管理器里的**缩略图和预览窗格
（Alt+P）直接显示还原后的画面**，角上带"混淆"角标（文字用 `[badge] text.obfuscated` 改），
不用解混淆就能快速看清内容；`preview = 0` 则显示噪点。改动即时生效。

说明：双击文件本身仍由系统照片应用打开（Windows 的双击按文件类型走文件关联，
无法按文件名拦截，点开是噪点属于正常）；要看内容请用资源管理器的预览窗格或缩略图。
识别规则：文件名以 `_混淆` 结尾，或 PNG 里带本插件的扰码元数据块（改过名的 keepmeta
混淆文件也能识别）；网页版工具混淆的 `obfuscated_*.png` 不在识别范围内。
隐私提醒：开启后任何浏览该文件夹的人都能直接看到画面，此开关与混淆的隐藏目的相悖，
请按需取舍。

命令行同样可用（支持多选，`--quiet` 把提示打到控制台）：

```bat
ComfyWorkflowMenu.exe obfuscate   "D:\output\ComfyUI_00042_.png"
ComfyWorkflowMenu.exe deobfuscate "D:\output\ComfyUI_00042__混淆.png" --quiet
```

关掉这两项：ini 里 `[menu] obfuscate = 0`；菜单文字可用 `menu.obfuscatelabel` /
`menu.deobfuscatelabel` 改。开关和文字即时生效，不用重装。

混淆/解混淆完成后，新文件通过 shell 变更通知**就地出现在当前打开的文件夹视图里**
（局部刷新，不弹新窗口、不跳选中项，当前选中的文件保持不变）。


## 调整角标的位置和大小

全部在 `ComfyShellExt.ini` 的 `[badge]` 段，改完**不用重装**，下一次生成缩略图就生效
（已经缓存过的文件要先跑 `clear_thumbnail_cache.bat`）。

| 键 | 含义 | 默认 |
| --- | --- | --- |
| `position` | 九个锚点之一 | `TopRight` |
| `scale` | 大小百分比，100 = 自动大小 | `100` |
| `margin` | 与边缘的距离，按缩略图**长边**的百分比 | `3` |
| `offsetx` / `offsety` | 微调偏移，长边百分比，正数向右 / 向下 | `0` |
| `textminpx` | 长边小于该值就改画纯色小块（列表视图放不下文字） | `88` |
| `text` | 未识别到具体工具时的角标文字 | `JSON` |
| `text.comfyui` 等 | 各工具的角标缩写，另有 `text.a1111` / `text.novelai` / `text.swarmui` / `text.fooocus` / `text.invokeai` | `Comfy` / `A1111` / `NAI` / `Swarm` / `Foo` / `Invoke` |
| `fill` / `border` / `textcolor` | AARRGGBB 颜色 | 深蓝底 + 青边 + 白字 |

九个锚点：

```
TopLeft      TopCenter      TopRight
MiddleLeft   Center         MiddleRight
BottomLeft   BottomCenter   BottomRight
```

规则：写了 `left`/`right` 就贴那一边，没写就水平居中；写了 `top`/`bottom` 就贴那一边，
没写就垂直居中。所以**上方正中间**就是：

```ini
[badge]
position = TopCenter
```

想再大一点、再往下一点：

```ini
position = TopCenter
scale = 150
offsety = 5
```

尺寸和距离都用**百分比**而不是像素，因为资源管理器会按视图大小索取 16 到 1024 像素
不等的缩略图，固定像素在小图上会糊成一团、在大图上会小得看不见。自动大小在 96px 缩略图
上约 11px 高、256px 上约 23px 高，`scale` 就是在这个基础上乘一个倍数。

改完想立刻看效果，不必装扩展也不必清缓存：

```bat
ComfyWorkflowDb.exe preview "D:\output\某张图.png" --size 256 -o test.png
```

（`preview` 读的是 exe 旁边那个 ini，所以直接改 `D:\Green\ComfyShellExt\ComfyShellExt.ini` 再跑就行。）

### 为什么默认在右上角

资源管理器会在**视频缩略图的右下角**画上默认播放器的小图标（那是 Explorer 自己叠的，
不是文件内容），角标放右下角会被它整块盖住。所以默认放右上角，图片也一样，保持两类
文件一致。

## 支持的格式与工作流所在位置

| 容器 | 读取位置 | 对应 ComfyUI 节点 |
| --- | --- | --- |
| PNG | `tEXt` / `zTXt` / `iTXt` 块的 `prompt`、`workflow`，以及 `eXIf` 块 | SaveImage |
| WebP | EXIF（任意 ASCII 标签，含 `workflow:{...}` 前缀写法）、XMP | SaveAnimatedWEBP |
| JPEG | EXIF `ImageDescription` / `UserComment` / `XPComment`、APP1 XMP、COM 注释 | 各类 Image Saver 自定义节点 |
| GIF | Comment 扩展块 | VideoHelperSuite |
| MP4 / MOV | `moov/udta/meta/ilst`（`©cmt` 等）与 `moov/meta` 的 mdta `keys` 表 | VideoHelperSuite |
| MKV / WebM | Matroska `Tags` → `SimpleTag`（`COMMENT`、`WORKFLOW`、`PROMPT`） | VideoHelperSuite |
| AVI | RIFF `LIST INFO` → `ICMT` | ffmpeg 输出 |
| FLAC | Vorbis comment | SaveAudio |
| 其他 | 兜底：扫描文件头尾各 8 MB 找工作流标记并括号配对切出 JSON | AVIF、加壳、奇怪的自定义节点 |

判定依据是内容而不是键名：JSON 里出现 `"class_type"`（API 格式）或
`"last_node_id"` / `"last_link_id"` / `"widgets_values"` / `"nodes"`+`"links"`
（编辑器图格式）才算命中。所以 A1111 的 `parameters` 文本不会被误判成 ComfyUI 工作流。

**载荷不完整也算命中**：如果容器把 JSON 截断了（EXIF/QuickTime 字段长度限制、写入
中断、压缩流损坏等），只要还能看到上面的特征字符串就照样打角标，并尽力恢复出能读的
部分——角标不会因为 JSON 不完整而消失。

## 视频（mp4/mkv/webm）没有角标

先跑 `ComfyWorkflowDb.exe diag`，看 `video clsid` 那行的 `DisableProcessIsolation`：

- 显示 `=1` → 注册正确，往下看第 2、3 步。
- 显示 `=-1` 或 `=0` → **重新以管理员身份运行一次 `install.bat`**。

原因值得解释一下：Windows 把缩略图处理器放在一个隔离的 `dllhost.exe` 里跑，而那个
隔离宿主**只支持用数据流初始化处理器**。图片可以纯靠流工作，所以图片角标一直没问题；
但视频不行——Windows 自带的视频缩略图处理器和 ffmpeg 都需要真实的文件路径。需要路径的
处理器必须在自己的 CLSID 下写 `DisableProcessIsolation = 1` 才能被资源管理器初始化，
否则它会被静默跳过，视频既没有缩略图也没有角标。1.1 版的 `install.bat` 会自动写这个值。

然后确认这两件事：

```bat
:: 1) 我们自己的代码能不能给这个文件出图（应该看到 workflow: YES 和结果尺寸）
ComfyWorkflowDb.exe preview "D:\output\video.mp4" -o mine.png

:: 2) 资源管理器实际会画成什么样（走 shell 自己的那条路）
ComfyWorkflowDb.exe preview "D:\output\video.mp4" --shell -o shell.png
```

- `mine.png` 有角标、`shell.png` 没有 → 注册或缓存问题：重跑 `install.bat`，
  再跑 `clear_thumbnail_cache.bat`，然后 `restart_explorer.bat`。
- 两个都没角标 → 是检测问题，用 `dump --tree` 看元数据在不在（见上一节）。
- `shell.png` 是**播放器图标**而不是视频画面 → Windows 解不了这个视频的码，这很常见。
  这种情况下我们按顺序尝试：Windows 自带处理器 → ffmpeg 抽帧 → **给那个播放器图标打角标**。
  也就是说图标外观保持不变，只是右上角多一个 `JSON`。
  想看视频画面而不是图标，装个 ffmpeg 或在 ini 里指定 `video.ffmpegpath` 即可。


## 某个文件没被识别出来怎么办

先用 `dump` 看清楚文件里到底有什么，它能区分"元数据根本不存在"和"存在但没被读出来"：

```bat
ComfyWorkflowDb.exe dump "D:\output\某个文件.png" --tree
```

输出分三段：容器结构（PNG 有哪些块 / MP4 有哪些 box）、找到的每一条文本元数据
（来源、长度、是否含 ComfyUI 特征、JSON 是否完整、前 200 字预览）、最终判定结果。

- 结构里**没有** `tEXt`/`iTXt`/`eXIf`（PNG）或 `udta`/`ilst`/`meta`（MP4）
  → 元数据确实不在文件里。常见原因：图片被截图/另存/压缩工具重新编码过、
  视频被剪辑软件重新封装过、或出图时 ComfyUI 带了 `--disable-metadata`。
- 有元数据条目但 `comfy=no` → 里面不是 ComfyUI 的东西（例如只有 A1111 参数）。
- 有条目且 `comfy=prompt/graph` 但结果是 no → 这是 bug，请把 `dump` 输出发我。
- 大文件、或工作流藏在少见的位置 → 加 `--deep` 全文件扫描（默认只扫头尾各 4 MB，
  另外对 MP4 会专门扫 `moov` 那一段，所以几 GB 的视频也能找到）。


## 命令行工具 ComfyWorkflowDb.exe

不需要安装 shell 扩展也能用。数据库默认放在 `%LOCALAPPDATA%\ComfyShellExt\db`，
可用 `--db <目录>` 指定别处。

```bat
:: 单个文件到底有什么元数据
ComfyWorkflowDb.exe check "D:\output\ComfyUI_00042_.png"

:: 没识别出来时用这个：列出每一条元数据 + 容器结构
ComfyWorkflowDb.exe dump "D:\output\ComfyUI_00042_.png" --tree
ComfyWorkflowDb.exe dump "D:\output\video.mp4" --tree --deep

:: 扫描输出目录建库（增量，大小和修改时间没变就跳过）
ComfyWorkflowDb.exe scan "D:\ComfyUI\output" --jobs 8

:: 找用过某个节点 / 某个模型 / 某段提示词的文件
ComfyWorkflowDb.exe find --node KSampler
ComfyWorkflowDb.exe find --model sd_xl_base_1.0.safetensors
ComfyWorkflowDb.exe find "赛博朋克"

:: 把某个文件的工作流导出成 json，可直接拖回 ComfyUI
ComfyWorkflowDb.exe get "D:\output\ComfyUI_00042_.png" -o wf.json
ComfyWorkflowDb.exe get "D:\output\ComfyUI_00042_.png" --prompt

:: 统计：多少文件带工作流、有几种不同的图、最常用的节点和模型
ComfyWorkflowDb.exe stats

:: 不装扩展也能看角标效果：渲染 Explorer 会显示的那张图
ComfyWorkflowDb.exe preview "D:\output" --size 256 --sheet sheet.png

:: 诊断：每个扩展名当前由谁处理；--plan 列出安装会改哪些注册表键
ComfyWorkflowDb.exe diag --plan

:: 自检：Explorer 会查询的 COM 接口是否齐备，并走一遍真实 COM 边界出图
ComfyWorkflowDb.exe comcheck "D:\output\ComfyUI_00042_.png"

:: 右键菜单的查看命令也可以直接调用，支持一次传多个文件（--quiet 把提示打到控制台）
ComfyWorkflowMenu.exe view   "D:\output\ComfyUI_00042_.png" "D:\output\00270-3534562570.png"
ComfyWorkflowMenu.exe export "D:\output\ComfyUI_00042_.png" --saveas

:: 图片混淆 / 解混淆：Gilbert 曲线像素重排，输出 <原名>_混淆.png / <原名>_还原.png
ComfyWorkflowMenu.exe obfuscate   "D:\output\ComfyUI_00042_.png"
ComfyWorkflowMenu.exe deobfuscate "D:\output\ComfyUI_00042__混淆.png"
```

数据库结构（纯文本，无需任何运行库）：

- `index.jsonl` —— 每个文件一行：路径、大小、修改时间、容器、是否有工作流、
  节点数、节点类型、模型名、提示词片段、命中来源。
- `objects\<前两位>\<sha1>.json` —— 提取出来的工作流 JSON 原文，按内容去重，
  所以一次出图批次的几百张图只占一份。

## 配置

改 `ComfyShellExt.ini`。改动 `[extensions]`、`[menu]` 后要重新跑一次 `install.bat`
（因为要重新接管文件类型 / 重建菜单注册），其余项在下次生成缩略图时立即生效。

常用项：

- `menu.dynamic` / `menu.staticverbs` —— 见上面"右键菜单"一节；
- `menu.viewlabel` / `menu.exportlabel` —— 菜单项文字；
- `badge.text` —— 角标文字，可以改成 `WF`、`工作流`；
- `badge.position` / `badge.scale` / `badge.margin` / `badge.offsetx` / `badge.offsety` —— 见上文"调整角标的位置和大小"；
- `badge.textminpx` —— 缩略图长边小于该值时改画纯色小块（列表 / 详细信息视图用）；
- `badge.fill` / `badge.border` / `badge.textcolor` —— AARRGGBB 颜色；
- `badge.iconcorner` —— 角标落在文件类型图标上时用的角落（默认右上）；
- `badge.oniconfallback` —— 无法出图时是否给文件类型图标打角标，保持 1；
- `video.ffmpegpath` —— 留空则依次找本目录、本目录 `bin`、`PATH`；
- `video.disableprocessisolation` —— 保持 1，否则视频角标不会工作；
- `log.enabled` —— 排错时置 1，日志写到 `%LOCALAPPDATA%\ComfyShellExt\ComfyShellExt.log`。

## 工作原理（以及为什么这样做）

Windows 没有"在缩略图角上贴标"的接口。图标覆盖层（`IShellIconOverlayIdentifier`）
只能贴左下角，而且全系统只有 15 个槽位。所以这里实现的是 `IThumbnailProvider`：

1. 安装时用 shell 自己的关联查找（`AssocQueryString` + `ASSOCSTR_SHELLEXTENSION`）
   问出每个扩展名**当前**由哪个处理器负责，记到
   `HKLM\SOFTWARE\ComfyShellExt\Originals`；
2. 从最不侵入的位置开始写入本扩展的 CLSID（`SystemFileAssociations\.ext` →
   `HKCR\.ext` → UserChoice ProgID → HKCR ProgID），每写一处就再问一次 shell
   是否已经解析到我们，一旦生效就停止，避免去动第三方播放器的 ProgID；
3. 每个被改动的键的原值都记进 `HKLM\SOFTWARE\ComfyShellExt\Backup\<.ext>`，
   `uninstall.bat` 按记录逐项还原；
4. 生成缩略图时，先向记录下来的**原处理器**要底图（图片是 Windows 的
   Photo Thumbnail Provider，视频是 shell32 的 Property Thumbnail Handler），
   所以画质和 Windows 原生完全一致，然后只在角上合成角标；
5. **没有工作流的文件原样返回原处理器给的位图**，一个像素都不改。

底图拿不到时的退路：图片走 GDI+ 解码；视频调用 ffmpeg 抽一帧
（`webm` / `vp9` 这类 Windows 自己经常出不了图的格式主要靠它）；两者都失败但确实
检测到工作流时，画一个带文件类型文字的深色占位块 + 角标——总比一个空白通用图标强，
不想要可以 `badge.placeholder = 0` 关掉。

元数据解析只读容器的头部结构，`mdat` 之类的数据块是跳过的，所以几个 GB 的视频
和几百 KB 的图片检测耗时差不多。

## 排错

| 现象 | 处理 |
| --- | --- |
| 角标没出现 | 先 `ComfyWorkflowDb.exe diag` 看扩展名是否显示 `ComfyShell`；再 `dump --tree` 那个文件看元数据在不在 |
| 右键没有菜单项 | Win11 在"显示更多选项"里，或按 Shift+右键；想进一级菜单见上文 `staticverbs` |
| 点了菜单项没反应 | `diag` 确认 version 是 1.2.0.0 或更高（1.1 有个 bug 让第一项失效）；再跑 `comcheck` 看 invoke offset 0 / 1 是否都是 decoded |
| 老文件没变化 | Windows 缩略图缓存，跑 `clear_thumbnail_cache.bat` |
| 改了 ini 没反应 | `[extensions]`、`[menu]` 的改动需要重跑 `install.bat`；其余项换一个未缓存的文件试 |
| 想看详细日志 | ini 里 `log.enabled = 1`，重启 explorer，再看 `%LOCALAPPDATA%\ComfyShellExt\ComfyShellExt.log` |
| 视频缩略图变空白 | 装个 ffmpeg 或在 ini 里指定 `video.ffmpegpath` |
| 视频没有角标 | `diag` 看 `DisableProcessIsolation` 是否为 1，不是就以管理员身份重跑 `install.bat`（原因见上文） |
| 想临时停用 | 跑 `uninstall.bat`，随时可以再 `install.bat` |

`regasm /codebase` 会对没有强名称的程序集给出 `RA0000` 警告，这是预期的，
apkshellext2 也是同样的注册方式。

## 已知限制

- 目录不能移动：`/codebase` 把绝对路径写进了注册表，移动后要重新 `install.bat`。
- AVIF / HEIC 的 EXIF 存在 `iloc` 间接寻址里，没有做完整解析，靠兜底扫描命中。
- 压缩包内的视频拿不到路径，不会有角标（Windows 自己也不给压缩包内视频出缩略图）。
- 扩展是 .NET Framework 4.x 的托管 COM。缩略图处理器跑在 Explorer 的 `dllhost.exe`
  缩略图宿主里，不在 explorer.exe 进程内；右键菜单处理器按 Windows 的设计是加载进
  explorer.exe 的，所以它只做两件事：读文件头判断有没有工作流、启动
  `ComfyWorkflowMenu.exe`，其余一律 try/catch 兜住。
- 右键菜单的动态处理器在 Windows 11 一级菜单里不可见（微软把旧式扩展统一移到了
  "显示更多选项"），这是系统行为不是 bug；需要一级菜单就用 `staticverbs = 1`。

## 从源码编译

需要 Visual Studio 2022（用它自带的 Roslyn `csc.exe`）或 Build Tools，不需要
.NET SDK、不需要 NuGet、没有第三方依赖。

```bat
build.bat                                   :: 输出到 dist\
ComfyWorkflowDb.exe selftest tests\fixtures :: 21 个样本的检测回归测试
```

`tools\make_fixtures.py` 用 Python + ffmpeg 生成测试样本（每种容器一个有工作流、
一个没有），`tests\fixtures\expected.json` 是期望结果。


