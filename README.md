# TOfont

嵌入式点阵字模提取工具，基于 .NET 9 + WinUI 3。

## 功能

- **文字取模** — 输入文字，选择字体和字号，生成点阵数据
- **图片取模** — 加载图片，支持裁剪、缩放后二值化取模
- **字体库** — 从 PTL 字符列表批量生成完整字库
- **字库导入** — 导入已有二进制字库进行查看和再导出
- 支持逐行 / 逐列 / 逐行进位 / 逐列进位 4 种扫描模式
- 支持高位在前 / 低位在前、阴码 / 阳码切换
- 输出 C51 / ANSI C 格式字节数组

## 项目结构

```
src/
├── TOfont.Core/       # 核心库 — 提取、格式化、解析
│   ├── Extraction/    # 文字取模、图片取模、点阵转换
│   ├── Formatting/    # 代码输出格式化
│   ├── Models/        # 数据模型
│   └── Parsing/       # PTL 解析、字库导入
├── TOfont.WinUI/      # WinUI 3 桌面应用
│   └── Pages/         # 主页、取模页、设置页
└── TOfont.Cli/        # 命令行工具
```

## 依赖

- .NET 9
- Windows App SDK / WinUI 3
- SkiaSharp
- System.Drawing.Common

## 构建

```bash
dotnet build
```
