# 归档区（`~` 后缀 = 只读历史，不参与当前工作）

本目录放**已被取代但必须留痕**的记录。Unity 不导入工程根 `Docs/`，此处 `~` 只是给人看的约定：进这里的东西不再修改。

## 内容

| 路径 | 是什么 | 时间 | 备注 |
|---|---|---|---|
| `legacy_10_DigitalTwin/PROGRESS.md` | 融合期的逐日进度流水（50.6 KB） | 2026-10-29 ~ 11-03 | 被 `../10_AGENT/SESSION_LOG.md` 取代；**只读**。源头仍留在 `Assets/DoosanDT/10_DigitalTwin/PROGRESS.md`，两边内容一致，后续不再更新源头 |
| `legacy_10_DigitalTwin/INTERFACES.md` | 数字孪生模块接口清单（17.2 KB） | 2026-11-03 | 仍可参考；源头在 Assets 内同步维护 |
| `legacy_10_DigitalTwin/README.md` | 模块说明（9.1 KB） | 2026-11-03 | 同上 |
| `legacy_AI_HANDOFF/` | 早期 AI 交接协议（LATEST/LINK/PROGRESS/PROTOCOL + 请求响应目录） | 2026-10-2x | 项目根的 `AI_HANDOFF/` 是另一 agent 的活协议目录，**未移动**，此处是快照副本 |
| `legacy_logs_20261103/` | 融合期的 MCP 日志（build/verify/guids/polish/audit） | 2026-11-03 | 原始日志在 `Logs/`，此处为当时快照 |

## 规矩

1. **只进不出**：归档后不再编辑；要改就在现行目录写新版本。
2. 归档文件名带日期或来源；不建 `_v2`、`_final` 之类副本。
3. 需要恢复某段历史时，从 `_backup\scenes\<日期>_<标签>\` 或本目录复制，不反向覆盖现行文件。