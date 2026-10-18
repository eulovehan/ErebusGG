Unicode true
!include "MUI2.nsh"
!include "x64.nsh"
!include "WinVer.nsh"
Name "Erebus GG"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\ErebusGG"
RequestExecutionLevel user
SetCompressor /SOLID lzma
VIProductVersion "${APP_VERSION}.0"
VIAddVersionKey "ProductName" "Erebus GG"
VIAddVersionKey "FileDescription" "Erebus GG Setup"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "Erebus GG contributors"
!define MUI_ICON "..\src\EternalGG.App\Assets\erebusgg.ico"
!define MUI_UNICON "..\src\EternalGG.App\Assets\erebusgg.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\ErebusGG.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Korean"

/** 지원 운영체제와 현재 사용자 범위 확인 */
Function .onInit
  SetShellVarContext current
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "Windows 10 또는 11의 x64 환경이 필요합니다"
    Abort
  ${EndIf}
  ${IfNot} ${AtLeastWin10}
    MessageBox MB_ICONSTOP "Windows 10 이상이 필요합니다"
    Abort
  ${EndIf}
FunctionEnd

/** 실행 파일과 의존성의 사용자별 설치 */
Section "Erebus GG"
  SetShellVarContext current
  SetOutPath "$INSTDIR"
  File /r /x *.pdb "${PUBLISH_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortCut "$SMPROGRAMS\Erebus GG.lnk" "$INSTDIR\ErebusGG.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG" "DisplayName" "Erebus GG"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG" "DisplayIcon" "$INSTDIR\ErebusGG.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG" "UninstallString" '$\"$INSTDIR\Uninstall.exe$\"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG" "QuietUninstallString" '$\"$INSTDIR\Uninstall.exe$\" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG" "NoRepair" 1
SectionEnd

/** 설치 파일 목록만 제거하고 사용자 설정 보존 */
Section "Uninstall"
  SetShellVarContext current
  Delete "$SMPROGRAMS\Erebus GG.lnk"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ErebusGG"
  !include "${UNINSTALL_MANIFEST}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
SectionEnd
