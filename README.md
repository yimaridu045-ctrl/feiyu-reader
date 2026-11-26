# 肥鱼阅读器（Feiyu Reader）

一个单文件、免安装的 Windows 本地阅读器：**文本 / 图片 / 文档 / 电子书**都能读，带**百度百科联网搜索**和**阅读进度记忆**。

界面按 DeepSeek Harness 的视觉风格实现（浅色/深色双主题、卡片式布局、蓝色主按钮）。

> 单文件 exe，约 250 KB，不依赖 .NET 以外的任何运行时（Windows 10/11 自带 .NET Framework 4.8）。

![主界面](docs/screenshots/main.png)

## 功能

### 阅读
- **文本**：txt / md / log / csv / json / xml / yml / ini / 字幕（srt/ass/vtt/lrc）/ 各类源码
- **文档与电子书**：docx、odt、epub、rtf（自动提取纯文本）
- **网页**：html / htm / xhtml（去掉脚本样式，只留正文）
- **图片**：png / jpg / bmp / gif / webp / tif / ico / wmf / emf（webp 走 WIC 解码）
- **编码识别**：UTF-8（含 BOM）、GBK、Big5、UTF-16 LE/BE，也可手动指定
- **章节目录**：识别「第X章」「Chapter N」「序章」「楔子」「番外」等，点目录跳转
- **阅读进度**：关闭后下次打开同一文件自动回到上次位置

### 最近阅读
- 不带参数打开时直接列出最近读过的文件，双击从上次位置继续
- 记录文件：`%APPDATA%\肥鱼阅读器\history.tsv`（时间戳 / 位置 / 大小 / 类型 / 路径）

### 联网搜索
- 顶部搜索条，结果列在左侧，双击用浏览器打开；`Ctrl+F` 聚焦，`Esc` 清空
- **百度百科词条**：百度官方接口对脚本请求全部拦截（搜索 302 验证页、词条页 403），
  因此改用 Bing 的 `site:baike.baidu.com` 限定取词条，并解码跳转链接还原真实百科地址
- 词条为空时回退维基百科；「浏览器」按钮直接用**百度搜索**
- 可选 Serper（Google）Key：填 `serperKey` 后优先用它，结果更全

### 图片查看
- 适应窗口 / 1:1 / 滚轮以光标为中心缩放 / 左键拖动平移 / 双击切换
- `←` `→` 或按钮在同目录图片间翻页

## 下载

到 [Releases](../../releases) 下载 `FeiyuReader-*-win-x64.zip`，解压后双击 `肥鱼阅读器.exe` 即可，**无需安装**。

## 从源码编译

只需要 Windows 自带的 .NET Framework 4.x 编译器（`csc.exe`）：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物在 `dist\肥鱼阅读器.exe`。手工编译命令见 `build.ps1` 内容（需要引用
`System.Windows.Forms`、`System.IO.Compression`、`System.Web.Extensions`，
以及 WIC 解码用的 `WindowsBase`/`PresentationCore`/`System.Xaml`）。

## 配置

配置文件：`%APPDATA%\肥鱼阅读器\config.ini`

```ini
fontSize=18                     ; 正文字号
theme=light                     ; light / dark
width=1080                      ; 窗口尺寸
height=780
proxy=                          ; 留空用系统代理；搜索装不上代理时会自动试 7877/7890/7897/10809
serperKey=                      ; 可选，Serper 的 API Key
smoothScroll=0                  ; 1 = 启用像素平滑滚动（有已知边界风险，默认关闭）
```

> 想彻底便携：把上面的目录内容搬到 exe 同级并设置环境变量 `FY_HISTORY_DIR` 指向该目录。

## 快捷键

| 按键 | 作用 |
|---|---|
| `←` `→` / `PageUp` `PageDown` / 空格 | 翻屏（图片模式下为上一张/下一张） |
| `+` `-` | 字号（图片模式下为缩放） |
| `0` / `1` | 图片：适应窗口 / 实际大小 |
| `T` | 切换浅色/深色 |
| `C` | 开关左侧面板 |
| `Ctrl+F` | 聚焦搜索框 |
| `Esc` | 清空搜索 / 从搜索结果或最近阅读返回目录 |
| `Ctrl+O` | 打开文件 |

## 已知问题

- **滚动**：当前使用 RichEdit 原生滚动 + 窗口 `WS_EX_COMPOSITED` 整窗合成。
  曾尝试用 `EM_SETSCROLLPOS` 做像素级平滑滚动，但越界值会让 RichEdit 丢失滚动位置
  （表现为跳/空白），因此默认关闭。详见 `docs/scroll-notes.md`。
- **pdf / doc / xls / ppt**：不支持，会给出提示（旧版 Office 为二进制格式，需要另存）。
- exe 为 DPI-unaware，在 125% 缩放的屏幕上由 Windows 位图放大，文字会略有模糊。

## 许可

[MIT](LICENSE) © 2026 yimaridu045-ctrl

图标为 AI 生成的原创角色形象（`assets/icon.ico`），不涉及第三方版权。

