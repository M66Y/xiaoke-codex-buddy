# 开发说明

小克陪伴使用 C# 与 WPF，基于 .NET Framework 4.8。当前版本为 v1.2。

## 构建与检查

在仓库根目录用 PowerShell 执行：

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1
```

构建脚本使用 Windows 自带的 .NET Framework C# 编译器，并将运行所需的程序、素材、配置和辅助脚本复制到：

```text
dist/Q版 Claude娘 Codex 额度查询-v1.2/
```

自检覆盖额度解析、无效值处理、缓存刷新、前台显示判断、外部点击收起、五分钟关心提醒和六秒无点击收起的边界条件。

## 目录结构

```text
src/
  OrangeQuota.cs          人偶、对话气泡、动画和额度读取
  app.manifest           Windows 应用清单与 DPI 设置
  OrangeQuota.exe.config .NET Framework 运行配置
assets/
  doll.png               透明背景人偶素材
scripts/
  Build.ps1              构建与复制运行文件
  Test.ps1               本地自检
  Enable-Autostart.ps1   注册当前用户登录启动项
  Disable-Autostart.ps1  移除登录启动项
  Start.vbs              无控制台启动入口
docs/
  preview.png            示例数据布局预览
```

## 额度读取

程序从 PATH 或当前用户的 `AppData/Local/OpenAI/Codex/bin` 查找 `codex.exe`，启动本机 `codex app-server`，完成初始化后调用 `account/rateLimits/read`。

参考：[Codex App Server 文档](https://learn.chatgpt.com/docs/app-server)。

显示的额度窗口及重置时间以服务端返回值为准，不假定账户始终提供固定数量的窗口。错误或缺失值不会替换为满额。

宿主在前台时，每分钟更新一次；15 秒内再次点击会复用缓存，手动刷新按钮可强制读取。更新失败时保留上次结果并显示错误。

## 交互约定

- Windows 前台宿主为 Codex 或 ChatGPT 时显示人偶，操作人偶自身窗口时保持可见。
- 离开宿主或最小化时，收起气泡、释放鼠标捕获并重置动画。
- 关心提醒按前台累计使用时间计时，不补弹后台积压的提醒。
- 无点击收起按 6 秒计时，后台额度更新不会延长显示时间。
- 鼠标检测仅在气泡显示时启用，不拦截原始点击。

位置保存在程序目录内的 `position-native.json`；额度缓存只存在于内存中。构建产物、位置文件和诊断输出均由 `.gitignore` 排除。
