Large Folder Finder
====================
A tool for quickly analyzing and listing folder hierarchies.
Useful for folder analysis using size conditions and filters (wildcards, regular expressions).
Helps identify the cause of issues in large data shared by multiple users, such as NAS.


■ How to Use
--------------------
  1. Select the folder you want to investigate.
  2. Press the "▶" (Scan) button to start the search.
  3. Results are displayed in a format similar to Windows Explorer.
  4. Specify display conditions: minimum size to extract, filter, sort, collapse folders.
  5. Press the copy button at the top right to copy the display results to the clipboard.
  6. Press the "+" button to the right of the tab to start a new scan while keeping the history.
    History is retained even after closing the application.
※ Running the app with administrator privileges allows you to analyze administrator-privileged folders within the C drive.
※ You can switch languages and change layouts from Menu/View.
※ You can change settings from Menu/View/Open Advanced Settings(S). Details are described below.


■ About Display Features
-------------------
1. Sort
  Click each label (Name, Size, Date Modified, Type) to sort the display order.
  Click again to toggle between ascending/descending order.
2. Show Files
  Check this to display files as well.
3. Minimum Size
  Specify the minimum size of folders or files to display. Items equal to or larger than the set value will be displayed.
  Enter 0 if you want to display everything.
  Units can be selected from Byte to TB.
4. Filter
Wildcard: Same behavior as Windows Explorer.
  * Allows matching any string. Example) *.txt All txt files with any name. Example 2) *data* All files with "data" in the name.
  ? Allows matching any single character. Example) 202?year → 2020year~2029year, etc. (matches non-digits too)
  ~ Place before (* or ?) to search for those characters themselves. Example) ~?.txt → Searches for ?.txt
Regular Expression: Advanced filter feature (used by engineers, etc.)
  Can do things that wildcards cannot. Match only numbers, lowercase letters, uppercase letters, extract only non-matching items, etc.
  It's complex, so please search for "how to use regular expressions" separately.
  There are also regular expression checker tools available to verify if your search is working correctly.
5. Space/Tab
  Specify whether to fill the space between name and size with spaces or tabs when pressing the copy button.


■ When It Doesn't Work Properly
------------------------
※ You can check the app's behavior from Menu/View/Logs.
※ If the app behaves strangely, deleting the data in the following folder may reset the cache and restore functionality.
    %LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder


■ Advanced Settings (Config.txt)
--------------------
By editing "Config.txt" in the execution directory, more detailed behavior settings are possible.
Click the "⚙" button on the UI to open it immediately with a text editor like Notepad.
Configuration must follow the YAML format. If you want to add your own comments, prefix them with #.

    ▽ Configurable items: (Default)
    UseParallelScan: true
        Type: bool (true/false)
        Description: Enable parallel processing
        Expected value (true): Effective for NAS (network storage). Local SSDs are fast, so parallelization overhead may be larger.

    SkipFolderCount: false
        Type: bool (true/false)
        Description: Whether to skip pre-counting for progress display and start scanning immediately
        If true, progress percentage cannot be displayed because the total number is unknown.

    MaxDepthForCount: 3
        Type: int (natural number)
        Description: Maximum hierarchy depth for pre-counting folders to determine progress percentage
        Larger specified hierarchy may take more time. Instead, progress accuracy improves.
        Expected value (3): NAS: 3~6, Internal PC: 7~

    UsePhysicalSize: true
        Type: bool (true/false)
        Description: Whether to calculate "allocated size on disk" considering cluster size
        Expected value (true): Usually recommended to keep true. Results will be closer to Windows property displays. If false, it calculates by file size.
        Before adjusting this, we recommend running as administrator. System files will be included in calculations for accuracy.

    OldDataThresholdDays: 30
        Type: int (Non-negative integer)
        Description: Highlights the tab in yellow to indicate old scan data if the specified number of days has passed.
        Expected Value: User preference.

■ How to Add Language Files
--------------------
This tool supports multiple languages, and you can add new ones.
1. Open the "Languages" folder in the same hierarchy as the app executable (.exe).
2. Copy an existing file like "en.yaml" and rename it to the culture code of the language you want to add (e.g., "fr.yaml" for French).
   * Refer to the following for a list of culture codes (e.g., ja-JP / ja):
   https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/available-language-packs-for-windows?view=windows-11
3. Edit the text within the YAML file (save in UTF-8 format).
4. Restart the app, and the new language will appear in the "Language" menu.
※ If necessary, create and add Readme_<language_code>.txt by referring to other files.


■ Complete Uninstall (Delete Settings and Logs)
--------------------
To completely remove settings and execution logs of this tool, please manually delete the following folder:
%LOCALAPPDATA%\Cat & Chocolate Laboratory\LargeFolderFinder
(You can open it directly by pasting the above path into the Explorer address bar)


■ Copyright
--------------------
Copyright (C) 2026 Ryoma Henzan / Cat & Chocolate Laboratory
