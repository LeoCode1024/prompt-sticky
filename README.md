# 提示词便签

当前版本：`1.1.0`

Windows 10/11 本地绿色版桌面小工具，用来快速保存、编辑、复制常用提示词。

## 构建

在项目目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出文件：

```text
dist\PromptSticky.exe
```

## 使用

- 鼠标移到图标上会展开便签。
- 展开时会有纸张卷轴式展开动画。
- 鼠标离开后约 0.5 秒自动收起。
- 单击任意单元格复制内容，成功后显示 `√`。
- 双击单元格编辑内容；双击时第一次点击仍会执行复制。
- 编辑时 `Shift+Enter` 换行，`Enter` 完成该单元格编辑。
- 顶部按钮使用图标显示，鼠标悬停会显示功能说明。
- 未锁定时可拖动图标或顶部区域，也可拖动右下角调整大小。
- 可切换置顶，让工具保持在所有窗口上方。
- 可通过同一个按钮导入或导出 CSV 文件。
- 数据自动保存到当前 Windows 用户目录，跟随当前设备，不再写入 exe。
- 工具不会显示在任务栏或 Alt+Tab 切换列表中。
- 工具会阻止重复启动，只保留一个运行实例。
- 工具图标使用生成的便签图标，资源已内嵌到 exe 中。

数据文件位置：

```text
%AppData%\PromptStickyTool\settings.xml
```

如果遇到异常，日志会写入：

```text
%AppData%\PromptStickyTool\error.log
```
