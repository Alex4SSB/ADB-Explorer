<p align="center">
  <img alt="ADB Explorer Logo" src="icons/Store_icon_2023.png" width="150px" />
  <h1 align="center">ADB Explorer</h1>
</p>

<a title="Platform" target="_blank">
	<img src="https://img.shields.io/badge/Platform-Windows-blue" alt="Platform" />
</a>

<p >
  <a href="https://github.com/Alex4SSB/ADB-Explorer/issues">
    <img alt="Issues" src="https://img.shields.io/github/issues/Alex4SSB/ADB-Explorer?color=0088ff" />
  </a>
  <img alt="GitHub last commit" src="https://img.shields.io/github/last-commit/Alex4SSB/ADB-Explorer?label=Last%20commit">
</p>

An interface to ADB that allows browsing, transferring, and editing of files with ease, in a modern and fluent Windows app, built in WPF.



<a href="https://www.microsoft.com/store/apps/9PPGN2WM50QB">
      <img alt="Issues" width=300px src="https://get.microsoft.com/images/en-us%20light.svg" />
</a>

<br></br>
<h2 align="left"/>App Files</h2>

`...\AppData\Local\IsolatedStorage\...`

Launch app while pressing L-Ctrl to open app files location which may contain:
* App.txt - persistent settings file.
* AdbProgressRedirection.exe - a pipe for getting progress updates while executing ADB push & pull commands.
* TEMP - a folder to which edited files are transferred temporarily.

The settings file contains user app settings as well as other settings not directly accessible.
The file can be edited, but the format must be preserved.
An unrecognized entry will be overwritten.
The file can be deleted to restore app settings to their defaults.