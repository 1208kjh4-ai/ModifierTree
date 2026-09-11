# 회사와 집에서 이어서 개발하기

공개 저장소: [1208kjh4-ai/ModifierTree](https://github.com/1208kjh4-ai/ModifierTree).

이 저장소는 소스, 문서, 테스트와 빌드 스크립트를 공유한다. `.tools/`, `artifacts/`, `bin/`, `obj/`는 각 PC에서 다시 만들며 Git에 올리지 않는다. Rhino SDK 파일도 저장소에 포함하지 않는다.

## 집 PC의 첫 설정

Windows x64와 Rhino 8을 준비하고 [Git for Windows](https://git-scm.com/install/windows)를 설치한다. 설치 후 터미널을 다시 연다. PowerShell에서 원하는 작업 폴더로 이동한 다음 저장소의 **Code → HTTPS** 주소로 복제한다.

```powershell
git clone https://github.com/1208kjh4-ai/ModifierTree.git
cd ModifierTree
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-sdk.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release
```

`setup-sdk.ps1`은 프로젝트가 사용하는 .NET SDK를 로컬 `.tools/`에 준비한다. 빌드는 설치된 Rhino 8 SDK를 참조한다. Rhino 설치 위치가 기본값과 다르면 빌드 인수에 `-RhinoSystemDir 'D:\Apps\Rhino 8\System'`처럼 실제 경로를 지정한다.

Rhino를 종료한 상태에서 플러그인을 설치한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-plugin.ps1
```

집 PC에 처음 등록하는 경우 스크립트가 출력하는 고정 경로의 `ModifierTree.rhp`를 Rhino에 한 번 로드한다. 이후에는 Rhino를 종료하고 설치 스크립트만 다시 실행한다. `MTreeStatus`로 현재 버전을 확인한다.

기존 문서의 `D:\Project_Modifier` 예시는 이 PC의 복제 폴더로 바꾸어 사용한다. 문서에 연결된 예전 `artifacts/` 검사 로그와 샘플은 로컬 산출물이므로 복제에 포함되지 않는다.

## 작업을 시작할 때

현재 회사 PC에는 Git과 GitHub CLI를 프로젝트의 `.tools/`에 준비하고 사용자 PATH에 등록했다. 새 터미널이나 IDE를 열면 `git`과 `gh`를 사용할 수 있다. 이 PC에서는 `.tools/git/`와 `.tools/gh/`를 유지한다. 집 PC에는 위 안내대로 Git for Windows를 별도로 설치한다.

```powershell
git status
git pull --ff-only
```

다른 PC에서 올린 변경을 먼저 받는다. 로컬 수정이 남아 있거나 브랜치가 갈라져 pull이 중단되면, 수정 내용을 보존한 채 충돌을 정리한다.

## 작업을 마치고 다른 PC로 이동하기 전

```powershell
git status
git diff
git add .
git diff --cached --stat
git commit -m "Describe the change"
git push
```

`commit`은 현재 PC에 변경을 기록하고, `push`가 GitHub에 업로드한다. 집에서 수정한 것도 push한 뒤 회사 PC에서 pull한다. 공개 저장소의 새 PC에서 push할 때는 본인의 GitHub 계정으로 로그인한다.

첫 commit에서 작성자 설정을 요청하면 아래 값을 본인 정보로 바꾼다. 이메일은 GitHub 계정에 등록된 주소를 사용한다. 원하는 경우 GitHub의 **Settings → Emails**에 표시된 noreply 주소도 사용할 수 있다.

```powershell
git config user.name "GitHub 사용자명"
git config user.email "GitHub 계정에 등록된 이메일"
```

코드를 수정한 뒤에는 Release 빌드와 관련 검사를 실행하고, Rhino를 종료한 뒤 설치 스크립트로 반영한다. `.3dm` 작업 파일은 현재 소스 저장소와 별도로 옮긴다.
