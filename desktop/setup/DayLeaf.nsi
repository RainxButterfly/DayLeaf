; ============================================================================
;  叶历 DayLeaf · Windows 安装包脚本
;
;  构建:  makensis /DVERSION=1.0.0 /DSRCDIR=... /DOUTFILE=... DayLeaf.nsi
;  由 desktop/frontend/build.ps1 自动调用，一般不用手跑。
;
;  按当前用户安装到 %LOCALAPPDATA%\Programs\DayLeaf，不弹 UAC。
;  日程数据存放在 %LOCALAPPDATA%\DayLeaf，卸载时保留。
; ============================================================================

Unicode true
SetCompressor /SOLID lzma
RequestExecutionLevel user
ManifestDPIAware true
ManifestSupportedOS all

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"

; ---- 构建参数 ----
!ifndef VERSION
  !define VERSION "1.0.0"
!endif
!ifndef SRCDIR
  !define SRCDIR "..\frontend\dist\DayLeaf-windows-1.0.0"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\frontend\dist\DayLeaf-Setup-1.0.0.exe"
!endif
!ifndef ICONFILE
  !define ICONFILE "..\frontend\res\app.ico"
!endif

; ---- 应用信息 ----
!define APP_NAME     "叶历 DayLeaf"
!define APP_EXE      "DayLeaf.exe"
!define PUBLISHER    "星河一叶Roxy"
!define APP_URL      "https://github.com/RainxButterfly/DayLeaf"
!define REG_UNINST   "Software\Microsoft\Windows\CurrentVersion\Uninstall\DayLeaf"
!define REG_APP      "Software\DayLeaf"

Name "${APP_NAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\DayLeaf"
InstallDirRegKey HKCU "${REG_APP}" "InstallDir"
BrandingText "${APP_NAME} ${VERSION}"
Icon "${ICONFILE}"
UninstallIcon "${ICONFILE}"

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=2052 "ProductName"      "${APP_NAME}"
VIAddVersionKey /LANG=2052 "FileDescription"  "${APP_NAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion"      "${VERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion"   "${VERSION}"
VIAddVersionKey /LANG=2052 "CompanyName"      "${PUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright"   "© 2026 ${PUBLISHER}"

; ---- 界面 ----
!define MUI_ABORTWARNING
!define MUI_ICON   "${ICONFILE}"
!define MUI_UNICON "${ICONFILE}"
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "立即打开 ${APP_NAME}"
!define MUI_FINISHPAGE_LINK "项目主页"
!define MUI_FINISHPAGE_LINK_LOCATION "${APP_URL}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

; ---- 语言字符串 ----
LangString DESC_Main    ${LANG_SIMPCHINESE} "程序本体与运行库，约 2 MB。"
LangString DESC_Desktop ${LANG_SIMPCHINESE} "在桌面也放一个快捷方式，方便直接打开。"

; ============================================================================
;  安装
; ============================================================================

; 覆盖安装/卸载前先请正在运行的实例自己关掉。
; 用窗口消息而不是 taskkill：不依赖外部命令，也不会强杀到一半留下锁文件。
Function CloseRunningApp
  FindWindow $0 "" "${APP_NAME}"
  ${If} $0 != 0
    SendMessage $0 ${WM_CLOSE} 0 0
    Sleep 800
  ${EndIf}
FunctionEnd

Function .onInit
  ; 已装过就沿用上次的安装位置
  ReadRegStr $0 HKCU "${REG_APP}" "InstallDir"
  ${If} $0 != ""
    StrCpy $INSTDIR $0
  ${EndIf}
FunctionEnd

Section "主程序" SecMain
  SectionIn RO

  ; 覆盖安装前先关掉正在运行的旧版本
  Call CloseRunningApp

  SetOutPath "$INSTDIR"
  File "${SRCDIR}\${APP_EXE}"
  File "${SRCDIR}\DayLeaf.exe.config"
  File "${SRCDIR}\WebView2Loader.dll"
  File "${SRCDIR}\Microsoft.Web.WebView2.Core.dll"
  File "${SRCDIR}\Microsoft.Web.WebView2.WinForms.dll"

  WriteUninstaller "$INSTDIR\uninst.exe"

  CreateDirectory "$SMPROGRAMS"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

  WriteRegStr HKCU "${REG_APP}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${REG_APP}" "Version" "${VERSION}"

  ; 「设置 → 应用」里显示的信息
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0

  WriteRegStr   HKCU "${REG_UNINST}" "DisplayName"     "${APP_NAME}"
  WriteRegStr   HKCU "${REG_UNINST}" "DisplayVersion"  "${VERSION}"
  WriteRegStr   HKCU "${REG_UNINST}" "Publisher"       "${PUBLISHER}"
  WriteRegStr   HKCU "${REG_UNINST}" "DisplayIcon"     "$INSTDIR\${APP_EXE},0"
  WriteRegStr   HKCU "${REG_UNINST}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKCU "${REG_UNINST}" "UninstallString" '"$INSTDIR\uninst.exe"'
  WriteRegStr   HKCU "${REG_UNINST}" "QuietUninstallString" '"$INSTDIR\uninst.exe" /S'
  WriteRegStr   HKCU "${REG_UNINST}" "URLInfoAbout"    "${APP_URL}"
  WriteRegDWORD HKCU "${REG_UNINST}" "NoModify" 1
  WriteRegDWORD HKCU "${REG_UNINST}" "NoRepair" 1
  WriteRegDWORD HKCU "${REG_UNINST}" "EstimatedSize" "$0"
SectionEnd

Section "创建桌面快捷方式" SecDesktop
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}    $(DESC_Main)
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} $(DESC_Desktop)
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; ============================================================================
;  卸载
; ============================================================================

Function un.CloseRunningApp
  FindWindow $0 "" "${APP_NAME}"
  ${If} $0 != 0
    SendMessage $0 ${WM_CLOSE} 0 0
    Sleep 800
  ${EndIf}
FunctionEnd

Section "Uninstall"
  Call un.CloseRunningApp

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\DayLeaf.exe.config"
  Delete "$INSTDIR\WebView2Loader.dll"
  Delete "$INSTDIR\Microsoft.Web.WebView2.Core.dll"
  Delete "$INSTDIR\Microsoft.Web.WebView2.WinForms.dll"
  Delete "$INSTDIR\uninst.exe"
  RMDir "$INSTDIR"

  ; 日程数据在 %LOCALAPPDATA%\DayLeaf，故意保留
  DeleteRegKey HKCU "${REG_UNINST}"
  DeleteRegKey HKCU "${REG_APP}"
SectionEnd
