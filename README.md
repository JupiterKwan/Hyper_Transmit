# Hyper Transmit

[![.NET](https://github.com/JupiterKwan/Hyper_Transmit/actions/workflows/dotnet.yml/badge.svg)](https://github.com/JupiterKwan/Hyper_Transmit/actions/workflows/dotnet.yml)

一款基于 WinUI 3 的现代 Windows SSH/SFTP 文件传输客户端，采用 MVVM 架构，提供直观的双窗格文件浏览器和高效的传输队列管理。

## ✨ 功能特性

- **SSH 连接管理** — 支持密码和私钥两种认证方式，可保存、编辑、删除连接配置
- **SFTP 文件传输** — 双窗格文件浏览器（本地 + 远程），支持上传、下载操作
- **传输队列** — 并发传输队列管理，实时显示传输进度和速度
- **连接管理器** — 集中管理所有已保存的连接，支持收藏、搜索、备注
- **安全存储** — 密码和私钥密码通过 DPAPI 加密存储，保障凭据安全
- **现代 UI** — 采用 Mica 透明材质背景，符合 Windows 11 设计规范
- **多架构支持** — 支持 x86、x64、ARM64 三种处理器架构

## 🖥️ 界面概览

| 页面 | 说明 |
|------|------|
| **连接** | 快速连接到 SSH 服务器，浏览和传输文件 |
| **管理** | 管理已保存的连接配置 |
| **队列** | 查看和管理文件传输任务队列 |
| **设置** | 应用程序配置（并发数、日志、主题等） |

## 🛠️ 技术栈

| 技术 | 版本 |
|------|------|
| .NET | 8.0 |
| Windows App SDK | 2.1.3 |
| WinUI 3 | — |
| CommunityToolkit.Mvvm | 8.4.0 |
| SSH.NET | 2024.1.0 |
| Serilog | 4.2.0 |
| Newtonsoft.Json | 13.0.3 |
| Microsoft.Extensions.DependencyInjection | 9.0.5 |

## 📋 系统要求

- **操作系统**: Windows 10 版本 1809 (10.0.17763.0) 或更高版本
- **运行时**: .NET 8.0 Desktop Runtime

## 🚀 构建与运行

### 前置条件

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 或更高版本（推荐），或 VS Code + C# Dev Kit

### 从命令行构建

```bash
# 还原依赖
dotnet restore

# Debug 构建 (x64)
dotnet build -c Debug -p:Platform=x64

# Release 构建 (x64)
dotnet build -c Release -p:Platform=x64

# Release 构建 (ARM64)
dotnet build -c Release -p:Platform=ARM64
```

### 运行

```bash
dotnet run -c Debug -p:Platform=x64
```

## 📦 发布

项目配置了独立部署（Self-Contained）的发布配置：

```bash
# 发布 x64 版本
dotnet publish -c Release -p:Platform=x64 -r win-x64

# 发布 ARM64 版本
dotnet publish -c Release -p:Platform=ARM64 -r win-arm64
```

发布产物位于 `bin/<Platform>/Release/<TargetFramework>/publish/` 目录下。

## 📁 项目结构

```
Hyper Transmit/
├── Assets/                      # 应用图标和启动画面资源
├── Converters/                  # XAML 值转换器
├── Models/                      # 数据模型
│   ├── Enums/                   # 枚举类型（认证方式、协议、传输状态等）
│   ├── ConnectionConfig.cs      # SSH 连接配置模型
│   ├── TransferTask.cs          # 传输任务模型
│   └── ...
├── Services/                    # 业务服务层
│   ├── Interfaces/              # 服务接口定义
│   ├── SshService.cs            # SSH 连接与命令执行服务
│   ├── TransferQueueService.cs  # 传输队列管理服务
│   ├── CredentialService.cs     # 凭据加密存储服务
│   ├── SettingsService.cs       # 应用设置持久化服务
│   └── Logger.cs                # 日志服务 (Serilog)
├── ViewModels/                  # MVVM 视图模型
│   ├── BaseViewModel.cs         # ViewModel 基类
│   ├── HomePageViewModel.cs     # 连接页面 ViewModel
│   ├── FileBrowserViewModel.cs  # 文件浏览器 ViewModel
│   ├── TransferQueueViewModel.cs# 传输队列 ViewModel
│   └── ...
├── App.xaml / App.xaml.cs       # 应用入口
├── MainWindow.xaml              # 主窗口（导航视图 + 状态栏）
├── HomePage.xaml                # 连接与文件浏览页面
├── ConnectionManagerPage.xaml   # 连接管理页面
├── TransferQueuePage.xaml       # 传输队列页面
├── SettingsPage.xaml            # 设置页面
└── Hyper Transmit.csproj        # 项目文件
```

## 🔧 VS Code 调试配置

项目在 `.vscode/launch.json` 中预置了 4 个调试配置：

- **C#: Debug x64** — 调试模式，x64 架构
- **C#: Debug ARM64** — 调试模式，ARM64 架构
- **C#: Release x64** — 发布模式，x64 架构
- **C#: Release ARM64** — 发布模式，ARM64 架构

## 🤖 关于本项目

本项目纯 **Vibe Coding**，使用 [MiMo-v2.5-Pro](https://github.com/XiaomiMiMo/MiMo) 模型驱动开发。
