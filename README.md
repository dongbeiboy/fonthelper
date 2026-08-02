# TOfont（toolbox）

嵌入式开发工具箱，基于 .NET 9 + WinUI 3，采用工具注册驱动导航的架构，可持续扩展新工具。

## 工具

- **字模提取** — 文字 / 图片取模、字体库生成、字库导入，支持多种扫描模式与位序切换
- **串口助手** — 调试助手（HEX / 文本收发）+ Shell 终端（类 PuTTY），支持 CLI HTTP 服务

## 构建

```bash
# 核心库与 CLI
dotnet build TOfont.sln

# WinUI 应用（Debug，x64）
dotnet build src/TOfont.WinUI/TOfont.WinUI.csproj -p:Platform=x64
```

> 构建前若应用正在运行，需先结束进程，否则 exe 被占用会导致构建失败。

## 文档

完整文档（功能说明、CLI 接口、重构方案等）见 [Wiki](https://github.com/dongbeiboy/toolbox/wiki)。

