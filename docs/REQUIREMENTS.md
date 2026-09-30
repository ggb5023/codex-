# Codex 自检修复器基线

## 支持范围

- Windows 10/11 x64
- Microsoft Store `OpenAI.Codex` 桌面应用
- 本地检测、受控修复、启动验证和报告导出
- 不修改 `.codex`、登录信息、VS Code 扩展或系统级环境变量

## 问题编号

| 编号 | 含义 | 当前处理 |
| --- | --- | --- |
| `PACKAGE_NOT_FOUND` | 未安装 Store 包 | 人工处理 |
| `PACKAGE_NOT_READY` | Store 包状态异常 | 人工处理 |
| `PACKAGE_ARCH_UNSUPPORTED` | 系统或包不是 x64 | 人工处理 |
| `CODEX_SIGNATURE_INVALID` | codex.exe 签名异常 | 人工处理 |
| `CODEX_CLI_PATH_INVALID` | CLI 环境变量失效 | 用户级可修复，系统级人工处理 |
| `RUNTIME_SOURCE_MISSING` | 商店包缺少运行时资源 | 人工处理 |
| `RUNTIME_STAGING_FOUND` | 存在未完成运行时部署 | 自动修复 |
| `RUNTIME_MATCHED` | 本地运行时与源资源匹配 | 正常 |
| `DISK_SPACE` | 运行时所在磁盘空间不足 | 人工处理 |

## 修复边界

修复前必须重新扫描并由用户确认。自动动作只包括关闭 Codex 相关进程、清除失效用户级 `CODEX_CLI_PATH`、备份旧运行时、复制并校验 `cua_node`、原子切换和双重启动验证。

权限不足、文件占用、杀毒软件拦截和 Microsoft Store 保护只收集证据并给出人工处理建议，不绕过系统安全策略。
