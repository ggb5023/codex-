# PowerShell 检测脚本

这些脚本用于在 Windows PowerShell 5.1 中单独验证 Codex 环境检测逻辑。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Get-CodexDiagnostic.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-CodexEnvironment.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-CodexRuntime.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-CodexDiagnostic.ps1
```

脚本只负责读取状态并输出 JSON，不会修改 Store 应用、用户数据或系统级环境变量。正式 GUI 仍由 C# 编排受控修复、备份、校验和启动验证。
