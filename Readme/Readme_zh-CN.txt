Large Folder Finder
====================
一个快速提取并列出大于指定大小的文件夹的工具。


■ 使用方法
--------------------
1. 选择您要调查的文件夹。
2. 指定您要提取的最小大小。
3. 点击“Scan”按钮开始搜索。
4. 结果以文本格式显示。
5. 点击右上角的复制按钮（📄 图标）将结果复制到剪贴板。


■ 高级设置 (Config.txt)
--------------------
通过编辑应用程序目录中的“Config.txt”，您可以配置详细的行为。
点击 UI 上的“⚙”按钮可立即使用记事本等文本编辑器将其打开。
配置必须遵循 YAML 格式。如果您想添加自己的注释，请在行首加上 #。

    ▽ 可配置项目：(默认值)
    UseParallelScan: true
        类型：bool (true/false)
        说明：启用并行扫描。
        背景 (true)：对 NAS（网络存储）等有效。由于本地 SSD 速度很快，并行化的开销可能会更大。

    SkipFolderCount: false
        类型：bool (true/false)
        说明：是否跳过进度显示的预计数并立即开始扫描。
        如果设为 true，则由于无法获知文件夹总数，进度百分比将无法显示。

    MaxDepthForCount: 3
        类型：int (自然数)
        说明：为了确定进度百分比，在扫描前执行文件夹总数计数的最大目录深度。
        较大的深度值可能会耗费更多时间，但会提高进度显示的准确度。
        示例 (3)：NAS: 3~6，本地 PC: 7~

    UsePhysicalSize: true
        类型：bool (true/false)
        说明：是否考虑簇大小来计算“磁盘上的实际占用大小”。
        示例 (true)：通常推荐设为 true。结果将更接近 Windows 属性显示的值。如果设为 false，则按文件实际大小计算。
        在调整此设置前，建议以管理员权限运行程序，以便准确地将系统文件纳入计算。


■ 如何添加语言文件
--------------------
本工具支持多种语言，您可以添加新语言。
1. 打开与可执行文件 (.exe) 位于同一层级的“Languages”文件夹。
2. 复制现有文件（如“en.yaml”），并将文件名更改为您要添加的语言的文化代码（例如，法语为“fr.yaml”）。
   * 请参考 Microsoft 文档查看文化代码列表：
   https://learn.microsoft.com/zh-cn/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. 编辑 YAML 文件中的文本（以 UTF-8 格式保存）。
4. 重启应用程序，新语言将出现在“Language”菜单中。
* 如有必要，请参考其他文件创建并添加 Readme_<code>.txt。


■ 完全卸载（删除设置和日志）
--------------------
如需完全删除本工具的设置和运行日志，请手动删除以下文件夹：
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
（您可以将上述路径粘贴到文件资源管理器的地址栏中直接打开）


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
