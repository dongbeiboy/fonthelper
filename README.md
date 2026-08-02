# TOfont（toolbox）

嵌入式开发工具箱，基于 .NET 9 + WinUI 3。采用工具注册驱动导航的架构，可以持续扩展新工具。

当前版本：**v1.0.2**

## 仓库结构

```
.
├── toolbox/   # 项目源码（src 目录，见下）
└── wiki/      # GitHub Wiki（文档）
```

## 工具列表

### 🅰️ 字模提取

- **文字取模** — 输入文字，选择字体和字号，生成点阵数据
- **图片取模** — 加载图片，支持裁剪、缩放后二值化取模
- **字体库** — 从 PTL 字符列表批量生成完整字库
- **字库导入** — 导入已有二进制字库进行查看和再导出
- 支持逐行 / 逐列 / 逐行进位 / 逐列进位 4 种扫描模式
- 支持高位在前 / 低位在前、阴码 / 阳码切换
- 输出 C51 / ANSI C 格式字节数组

### 🔌 串口助手

- **调试助手** — 串口配置（端口 / 波特率 / 数据位 / 停止位 / 校验位），HEX / 文本收发，多字节分包缓冲，GBK / UTF-8 编码切换
- **Shell 终端** — 类 PuTTY 交互式终端，行模式（回车发送、↑↓ 命令历史）与字符直通模式，Windows Terminal 风格深灰配色
- USB 拔插自动检测，端口列表自动刷新

## 项目结构

```
toolbox/src/
├── TOfont.Core/       # 核心库 — 提取、格式化、解析
│   ├── Extraction/    # 文字取模、图片取模、点阵转换
│   ├── Formatting/    # 代码输出格式化
│   ├── Models/        # 数据模型
│   └── Parsing/       # PTL 解析、字库导入
├── TOfont.WinUI/      # WinUI 3 桌面应用
│   ├── Framework/     # 工具箱框架（ToolCatalog / ToolDescriptor）
│   ├── Tools/         # 工具页（FontExtraction / SerialPortTool）
│   └── Pages/         # 首页、设置页
└── TOfont.Cli/        # 命令行工具
```

## 依赖

- .NET 9
- Windows App SDK / WinUI 3
- SkiaSharp
- System.Drawing.Common
- System.IO.Ports
- System.Text.Encoding.CodePages

## 构建

```bash
# 核心库与 CLI
cd toolbox && dotnet build TOfont.sln

# WinUI 应用（Debug，x64）
cd toolbox && dotnet build src/TOfont.WinUI/TOfont.WinUI.csproj -p:Platform=x64
```

> 提示：构建前若应用正在运行，需先结束进程，否则 exe 被占用会导致构建失败：
> `Get-Process TOfont.WinUI -ErrorAction SilentlyContinue | Stop-Process -Force`

## 发布

推送 `v*` 格式的 tag 即触发 GitHub Actions 自动构建发布：

```bash
git tag v1.0.2 && git push origin v1.0.2
```

产物：`TOfont-win-x64.zip`（自包含，免装 .NET 运行时），发布说明自动汇总提交历史。
