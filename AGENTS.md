# AGENTS.md — Unity_Ros_Try1（Token 护栏）

论文工程：虚实联动退役动力电池拆解实训。本仓库仅跟踪**自写代码**；场景/模型/缓存不进 Git。

## 启动三件套（宿主总控）

先读 `F:\Doosan_DT-VR_Lab\DoosanLab_VMware\`：

1. `AGENTS.md` + `CURRENT_STATE.md` + `agent_journal/LATEST.md`
2. `CODEBASE_MAP.md`（跨端路径/话题）
3. 需要写论文证据时再开 `THESIS_EVIDENCE.md`

## Token 护栏（写死）

| 允许 | 禁止 |
|---|---|
| 读写 `Assets/DoosanDT/**/Scripts/**/*.cs` | 全文读取 `.unity` / `.prefab` / `.meta` |
| 读写同树下 `Editor/**/*.cs`（仅改脚本逻辑时） | 把 `Library/` `Temp/` `Obj/` `Logs/` `Build/` 读进上下文 |
| 对场景/Prefab/meta **仅允许关键词 grep**（定位 GUID/字段名） | 整文件 dump 场景 YAML |
| 读 `Docs/10_AGENT/*.md`、本 `AGENTS.md` | 擅自运动真机 / 改 `allowed_joint_names_csv` |

> 历史路径 `Assets/Scripts/` 已迁到 `Assets/DoosanDT/`；**不要重建**旧目录。

## 关键联调（只读提醒）

- TCP：`rosTcpHost=192.168.205.128`，`rosTcpPort=10002`
- 运动锁：VM 侧 `allowed_joint_names_csv="none"`；本工程不得绕过
- MCP：工程听 **8091**（见 `ProjectSettings/McpUnitySettings.json`）；宿主 CLI：`DoosanLab_VMware/scripts/windows/unity_mcp_8091.mjs`

## 收工

在总控台跑：`scripts/windows/new_agent_journal.ps1 -Agent <name> -Slug <slug> -Summary "..."`  
（自动写入双端 Git commit）。
