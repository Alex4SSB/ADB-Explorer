<p align="center">
  <img alt="ADB Explorer Logo" src="icons/Store_icon_2023.png" width="150px" />
  <h1 align="center">ADB Explorer</h1>
</p>


<p align="center">
	<a href="https://hosted.weblate.org/engage/adb-explorer/">
		<img src="https://hosted.weblate.org/widget/adb-explorer/resources/svg-badge.svg" alt="Translation status" /></a>
	<br></br>
	<a title="Platform" target="_blank">
		<img src="https://img.shields.io/badge/Platform-Windows-blue" alt="Platform" /></a>
 	<br></br>
	<a href="https://github.com/Alex4SSB/ADB-Explorer/issues">
		<img alt="Issues" src="https://img.shields.io/github/issues/Alex4SSB/ADB-Explorer?color=0088ff" style="display:block;"/></a>
	<a href="https://github.com/Alex4SSB/ADB-Explorer/releases">
		<img alt="Downloads" src="https://img.shields.io/github/downloads/Alex4SSB/ADB-Explorer/total" /></a>
	<a href="https://github.com/Alex4SSB/ADB-Explorer/commits">
  		<img alt="GitHub last commit" src="https://img.shields.io/github/last-commit/Alex4SSB/ADB-Explorer?label=Last%20commit"></a>
</p>

An interface to ADB that allows browsing, transferring, and editing of files with ease, in a modern and fluent Windows app, built in WPF.



<a href="https://www.microsoft.com/store/apps/9PPGN2WM50QB">
      <img alt="Issues" width=300px src="https://get.microsoft.com/images/en-us%20light.svg" />
</a>

<br></br>
<h2 align="left"/>Translation</h2>
Help with translation is most welcome - please visit our translation project on Weblate: https://hosted.weblate.org/engage/adb-explorer/

<br></br>
<h2 align="left"/>App Files</h2>

`%LocalAppData%\AdbExplorer`


### ℹ️ For Store version (previous versions): <br />To be able to see these files, create `%LocalAppData%\AdbExplorer\App.txt` <br /> *before installing*, otherwise the files are stored in an unknown location.


* App.txt - persistent settings file.
* AdbProgressRedirection.exe - a pipe for getting progress updates while executing ADB push & pull commands.
* TEMP - a folder to which edited files are transferred temporarily.
* TempDrag... - folder(s) to which files are temporarily transferred for Drag & Drop / Clipboard transfer to PC.

The settings file contains user app settings as well as other settings not directly accessible.
The file can be edited, but the format must be preserved.
An unrecognized entry will be overwritten.
The file can be deleted to restore app settings to their defaults.

<h2 align="left"/>Compiling AdbProgressRedirection for x64</h2>
To minimize false positive AV detection of AdbProgressRedirection.exe, the x64 build is not done in Visual Studio, but using a specific version of UCRT. <br />
This leaves us with only Bkav Pro detection which is (aparently) infamous for its false positives. <br />
<br />

1. [Download GCC 13.3 with UCRT64](https://github.com/brechtsanders/winlibs_mingw/releases/tag/13.3.0posix-11.0.1-ucrt-r1)
2. Extract somewhere
3. Add to environment Path `[extracted path]\mingw64\bin`
4. Open the AdbProgressRedirection folder in VS Code
5. Build (Ctrl+Shift+B)
