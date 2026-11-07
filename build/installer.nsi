Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

Name "DiskCheck TW"
; publish.sh 以 -DPAYLOAD / -DOUTEXE 傳入原生格式的絕對路徑（Windows 上 NSIS 對相對路徑加 / 的萬用字元找不到檔案）
!ifndef PAYLOAD
  !define PAYLOAD "../publish/DiskCheckTW/*"
!endif
!ifndef OUTEXE
  !define OUTEXE "../publish/DiskCheckTW-0.1.3-setup.exe"
!endif
OutFile "${OUTEXE}"
InstallDir "$PROGRAMFILES64\DiskCheck TW"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
; 第一個語言為預設；Windows 介面為繁體中文時自動用中文，其他語言用英文。
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "TradChinese"
LangString UninstallLink ${LANG_ENGLISH} "Uninstall DiskCheck TW"
LangString UninstallLink ${LANG_TRADCHINESE} "解除安裝"
LangString DesktopSection ${LANG_ENGLISH} "Desktop shortcut"
LangString DesktopSection ${LANG_TRADCHINESE} "桌面捷徑"

Section "DiskCheck TW" SEC_MAIN
  SectionIn RO
  SetRegView 64
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\DiskCheck TW" "InstallDir" "$INSTDIR"
  CreateDirectory "$SMPROGRAMS\DiskCheck TW"
  CreateShortCut "$SMPROGRAMS\DiskCheck TW\DiskCheck TW.lnk" "$INSTDIR\DiskCheckTW.App.exe"
  CreateShortCut "$SMPROGRAMS\DiskCheck TW\$(UninstallLink).lnk" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskCheckTW" "DisplayName" "DiskCheck TW"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskCheckTW" "DisplayVersion" "0.1.3"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskCheckTW" "Publisher" "DiskCheck TW"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskCheckTW" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskCheckTW" "EstimatedSize" $0
SectionEnd

Section /o "$(DesktopSection)" SEC_DESKTOP
  CreateShortCut "$DESKTOP\DiskCheck TW.lnk" "$INSTDIR\DiskCheckTW.App.exe"
SectionEnd

Section "Uninstall"
  SetRegView 64
  ExecWait '"$SYSDIR\schtasks.exe" /Delete /TN "DiskCheck TW\定期檢測" /F'
  SetShellVarContext all
  Delete "$APPDATA\DiskCheck TW\schedule.json"
  RMDir "$APPDATA\DiskCheck TW"
  SetShellVarContext current
  Delete "$DESKTOP\DiskCheck TW.lnk"
  Delete "$SMPROGRAMS\DiskCheck TW\DiskCheck TW.lnk"
  ; 兩種語言的捷徑名稱都刪（安裝時語言可能與解除安裝時不同）
  Delete "$SMPROGRAMS\DiskCheck TW\解除安裝.lnk"
  Delete "$SMPROGRAMS\DiskCheck TW\Uninstall DiskCheck TW.lnk"
  RMDir "$SMPROGRAMS\DiskCheck TW"
  !include "uninstall-files.nsh"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\DiskCheckTW"
  DeleteRegKey HKLM "Software\DiskCheck TW"
SectionEnd
