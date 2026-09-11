# Modifier Tree — Rhino 8

Rhino 객체를 GUID로 등록하고, 사용자가 직접 순서와 부모·자식 관계를 편집하는 C# Modifier Tree 플러그인입니다.
현재 **0.15.2**는 **Modifier Manager에서 한 행을 선택하면 Rhino에서도 자동으로 선택하고 검볼을 활성화**합니다. Modifier는 전체 결과, 하위 객체·Control Box·BasePlane은 해당 항목을 선택합니다. Bend Control Box의 휘어진 윤곽선과 단면선, 박스별 Strength·Limited/Unlimited, 생성·Fit, 검볼 변형과 여러 박스의 연속 굽힘을 지원합니다. 최종 결과의 Rhino 선택·스냅·검볼, 원본 숨김, 이동 후 Undo의 결과 중복 방지, 기존 Boolean·Mirror·Array 및 Array Enter 입력도 유지합니다. 이름·설정·제어 객체 연결·트리를 `.3dm`에 저장하며, 기존 0.9.0~0.15.1 파일도 읽을 수 있습니다.
평소에는 최종 결과를 나타내는 작업용 Rhino Brep을 선택하고 스냅합니다. 이 결과를 Move·Rotate·Gumball로 조작하면 숨겨진 원본과 Array 축·BasePlane·Control Box도 함께 변환합니다. 내부 편집에 들어가면 그 단계의 입력만 노출합니다. 작업용 결과는 트리에서 다시 계산되며, 고정된 객체가 필요하면 Bake/Merge를 사용합니다.

## 개발 환경과 빌드

회사와 집에서 번갈아 작업하는 복제·빌드·업로드 순서는 [Git 작업 안내](MD/Git_Workflow.md)를 참고합니다.

- Windows x64, 최신 검사는 RhinoCommon 8.35.26251.13001에서 실행했습니다.
- 대상 런타임은 .NET 8 (`net8.0-windows`)입니다.
- 로컬 SDK는 `.tools/dotnet`, 버전은 `8.0.424`입니다.
- 설치된 Rhino의 `System/netcore/RhinoCommon.dll`, `System/Eto.dll`을 직접 참조합니다.
- 외부 NuGet 패키지는 사용하지 않습니다.

SDK 준비가 필요한 PC에서는 프로젝트 루트에서 실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-sdk.ps1
```

최종 배포용 빌드:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release
```

빌드 스크립트는 Core·이름·트리 교체·저장 상태·선택 범위·재계산 대기·플러그인 GUID 검사도 실행합니다. 출력 경로:

```text
artifacts/bin/0.15.2/Release/net8.0-windows/ModifierTree.rhp
```

같은 폴더의 `ModifierTree.Core.dll` 등 동반 파일을 함께 유지해야 합니다.
Configuration을 생략하면 Debug 폴더에 빌드합니다. Rhino가 로드한 파일은 잠길 수 있으므로 해당 빌드를 덮어쓰려면 작업을 저장하고 Rhino를 종료합니다.
Rhino 설치 위치가 다르면 빌드 인수에 `-RhinoSystemDir 'D:\Apps\Rhino 8\System'`을 추가합니다.

## 실행과 트리 편집

1. 기존 작업을 저장하고 Rhino를 완전히 종료한 뒤 아래 설치 스크립트를 실행합니다. 이후 Rhino를 시작합니다. 기존 등록이 있으면 경로를 갱신하므로 `.rhp`를 다시 드래그하지 않습니다.
2. `MTreeStatus`에서 버전 `0.15.2.0`을 확인하고 `MTree`로 패널을 엽니다.
3. **+ Add Object**로 원본 객체를 등록합니다. 객체는 최상위에 표시됩니다.
4. **+ Add Modifier**에서 **Boolean Difference / Union / Intersection / Mirror / Array / Bend**를 선택합니다. Modifier가 최상위에 생성됩니다. Mirror에는 전용 BasePlane이 함께 만들어지며, 연산할 원본은 사용자가 넣습니다. Bend에는 입력을 넣고 **Add Control Box**로 제어 박스를 추가합니다.
5. 원본 행을 Modifier 행 위로 드래그하여 자식으로 넣습니다. Difference는 **첫 번째 입력에서 나머지 입력들을 뺍니다**. Union은 모든 입력을 합치고, Intersection은 모든 입력에 공통인 부분을 남깁니다.
6. 행 사이에 드롭하면 순서를 바꾸고, 다른 Modifier 위에 드롭하면 부모를 바꿉니다. Modifier 자체도 다른 Modifier의 자식이 될 수 있습니다.
7. 하단 **Drop here to move to root** 영역에 드롭하면 최상위로 꺼냅니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-plugin.ps1
```

설치 스크립트는 최신 Release 빌드를 `artifacts/installed/ModifierTree/`라는 고정 경로로 복사하고, 기존 ModifierTree의 Rhino 8 사용자 등록 경로만 갱신합니다. 최초 설치라 등록이 없으면 출력되는 고정 경로의 `.rhp`를 한 번 드래그합니다. 업데이트 시에도 Rhino를 종료하고 이 스크립트를 사용합니다.
이전 고정 경로의 파일과 등록 경로는 `artifacts/install-backups/`에 백업합니다. 실행 중인 Rhino가 있으면 아무것도 변경하지 않고 중단합니다.
버전별 `.rhp`는 같은 플러그인 ID를 사용합니다. 이전 경로가 등록된 상태에서 새 경로를 별도로 로드하면 `ID already in use`가 발생할 수 있으므로 버전별 파일을 반복해서 드래그하지 않습니다.

```text
Boolean Difference
  Brep A
  Brep B
  Brep C
```

위 구조는 A에서 B와 C를 뺍니다. A/B 역할 접두사는 붙이지 않으며, Rhino 객체 이름을 그대로 표시합니다.
Modifier는 여러 개 생성할 수 있습니다. 중첩 시 자식 Modifier의 결과가 부모의 입력이 되며 최상위 Modifier 결과만 표시합니다.
자신 또는 자신의 자손 안으로 이동하는 순환 구조는 허용하지 않습니다.

**첫 행 클릭 → Shift를 누르고 마지막 행 클릭**으로 범위를 선택하고, 선택한 행 중 하나를 잡아 Modifier 위로 드래그하면 함께 들어갑니다. **Ctrl+클릭**으로 떨어진 행들을 추가·제외할 수도 있습니다. 선택한 순서에 관계없이 현재 트리의 위아래 순서를 유지합니다. 행 사이 재정렬, 다른 부모로 이동, 루트로 꺼내기에도 적용합니다.

패널의 **단일 행 선택은 Select in Rhino와 같은 동작**입니다. Modifier 행은 결과 전체를, 원본·제어 객체 행은 해당 편집 단계의 객체를 선택하고 검볼 활성화를 요청합니다. 여러 행 선택은 기존 트리 편집용 배치를 유지하며 Rhino의 다중 객체 선택으로 전환하지 않습니다. 화면 갱신·Undo·뷰포트 선택을 패널에 반영할 때는 자동 선택을 재실행하지 않습니다. [패널 선택 확인 순서](MD/Panel_Selection_Smoke_Test.md).

부모와 자식을 함께 선택하면 부모를 기준으로 이동하여 내부 구조를 유지합니다. 이동할 수 없는 항목이 있으면 전체 드롭을 취소하며, 묶음 이동은 Undo 한 번으로 되돌립니다. 다중 선택은 트리 배치에 적용하고 이름 편집·Select in Rhino·MTreeMove·Remove·Bake/Merge 등은 한 행을 선택해 사용합니다. [0.11.0 다중 선택과 Boolean 확인 순서](MD/Multi_Select_Boolean_Smoke_Test.md)를 참고하세요.

**Remove**로 원본 행을 제거하면 등록만 해제하고 Rhino 객체는 유지합니다.
Modifier를 제거하면 일반 자식들을 같은 위치의 상위 수준으로 꺼내고, 전용 제어 객체는 같은 위치에 숨깁니다. Control Box 하나만 제거할 수도 있으며 BasePlane의 단독 제거는 지원하지 않습니다.
같은 Rhino 객체를 다시 등록해도 중복되지 않습니다. 원본 이름 변경은 패널에 반영되며, 삭제된 원본은 `Missing`으로 남습니다.

원본의 **Name 셀을 더블클릭**하면 인라인으로 이름을 편집합니다. Enter/편집 완료 시 Rhino 원본 이름을 변경하며 Esc는 취소합니다. 이름 변경은 Undo할 수 있고 원본 GUID와 트리 구조는 유지됩니다.
**InputWire 셀을 더블클릭**하면 해당 원본의 지속적인 선 표시를 ON/OFF로 전환합니다. 이 설정은 선을 그리는 용도로만 사용하며, 숨긴 원본의 선택·스냅을 켜지 않습니다. 원본을 실제로 조작하려면 내부 편집에 들어가거나 해당 행에서 **Select in Rhino**를 누릅니다.

Add Object/Add Modifier와 Select in Rhino/Duplicate subtree는 세로로 배치되어 패널 너비를 채웁니다. **Bake·Merge·Remove는 한 가로줄에 동일 너비**로 배치하며, Move 버튼은 제거했습니다. 이동은 뷰포트의 검볼이나 Rhino Move로 수행할 수 있고 기존 `MTreeMove` 명령도 유지합니다. 트리는 남은 세로 공간을 사용하며 열 너비도 패널에 맞게 조절됩니다. 속성과 작업 버튼 영역은 따로 세로 스크롤하여 좁거나 낮은 패널에서도 트리 공간을 유지합니다. 속성 입력 오류는 속성 영역 아래에 표시하며, 계산 오류는 선택한 행의 툴팁 또는 Rhino 명령 기록에서 확인합니다. UI를 직접 수정할 파일·줄 위치는 [UI 수정 안내](MD/UI_Editing_Guide.md)를 참고합니다.

## ON/OFF와 속성 편집

Modifier 또는 Control Box 한 행을 선택하면 하단에 해당 종류의 속성이 나타납니다. 여러 행이나 일반 원본 행을 선택하면 속성 편집 영역을 숨깁니다. Array는 **Count·Spacing의 X/Y/Z 입력칸에서 Enter**를 누르면 현재 입력값을 한 번에 검증하고 즉시 적용하며, Apply 버튼은 표시하지 않습니다. Control Box도 **Strength에서 Enter**로 적용하고, Limited/Unlimited 선택은 즉시 적용합니다. **Name도 Enter**로 적용할 수 있습니다. 다른 Modifier의 Apply 버튼은 유지합니다. 숫자를 입력하는 동안이나 Tab으로 이동할 때는 적용하지 않으며, 잘못된 수치는 오류를 표시하고 기존 설정을 유지합니다. 적용 전 입력값은 일반 화면 갱신으로 지워지지 않으며, 다른 행을 선택하면 새 선택의 저장된 설정을 불러옵니다. 기존 Name 셀 더블클릭도 사용할 수 있습니다.

**Enabled** 체크박스는 즉시 ON/OFF를 적용하며 **State 셀 더블클릭**도 같은 동작입니다. OFF인 Modifier는 흐리게 표시하고 State에 `OFF`를 붙입니다. OFF에서는 트리를 유지한 채 **첫 번째 자식의 현재 결과만 상위에 전달**합니다. 나머지 자식은 이 연산에서 제외하지만 행·원본·InputWire 설정은 보존합니다. 자식 순서를 바꾸면 OFF 상태에서 전달하는 입력도 바뀝니다. 첫 자식이 없으면 `Needs inputs`, 첫 결과가 비면 Empty입니다. ON으로 복귀하면 현재 모든 입력으로 다시 계산합니다.

ON/OFF는 **Show result** 및 **InputWire**와 별도 설정입니다. Show result는 문서 전체의 결과 미리보기 표시를, InputWire는 개별 입력의 지속적인 선 표시를 조절합니다. OFF에서도 입력 선택·편집은 가능합니다.

## Bend와 Control Box

1. **Add Modifier → Bend**를 만들고 닫힌 Brep/Extrusion 또는 하위 Modifier를 넣습니다.
2. Bend를 선택하고 속성의 **Add Control Box**를 누릅니다. 현재 입력 결과를 감싸는 박스가 생성되고 선택됩니다. 입력이 없으면 원점에 한 변 10 문서 단위의 기본 박스를 만듭니다.
3. Control Box 속성에서 **Strength**를 입력한 뒤 Enter를 누릅니다. 기본값은 **45° / Limited**, 지원 범위는 **−180°~180°**입니다. 0°는 변형하지 않고 음수는 반대 방향으로 굽힙니다.
4. 검볼로 박스를 이동·회전·직교 확대/축소합니다. 박스의 **로컬 Y가 길이 축**, **로컬 X가 굽히는 방향**입니다. **Fit to inputs**는 현재 박스 방향을 유지하여 Bend의 입력 결과에 다시 맞춥니다.
5. 박스를 더 추가하고 트리에서 순서를 바꿉니다. Control Box는 위에서 아래로 이전 결과에 순차 적용되며, 다른 Bend로도 옮길 수 있습니다. 루트나 다른 종류의 Modifier로 옮길 수는 없습니다.

Limited는 로컬 Y 높이 구간을 굽히고 구간 밖을 끝단에 따라 직선으로 연결합니다. Unlimited는 높이 밖에도 같은 곡률을 적용합니다. X/Z 폭은 영향 범위를 잘라내지 않습니다. 별도 Angle·Within Box·Keep Y-Axis Length 토글은 없습니다. **길이 유지는 각 박스의 중립축 호길이 기준**이며 여러 박스 적용 후 임의 경로의 총길이까지 고정하는 뜻은 아닙니다.

Bend 전체를 선택하면 모든 입력과 박스가 함께 변환됩니다. 박스 행에서 **Select in Rhino** 또는 뷰포트의 내부 편집으로 들어가면 박스만 조작할 수 있습니다. Control Box는 선으로 표시하고, 일반 작업에서는 실제로 숨겨 원본과 함께 스냅 대상에서 제외합니다. Bake/Merge에는 굽힌 형상만 들어가며 제어 박스의 위치·크기는 바뀌지 않습니다. 복제는 박스와 설정까지 독립적으로 복제합니다.

**휘어진 Control Box 표시:** 네 모서리의 곡선, 중간 단면선, 중립축을 각 박스의 Strength로 굽혀 표시합니다. 선택 시 노란색, 그 외 표시된 박스는 하늘색입니다. 여러 박스는 각자 자기 설정만 표시하며 다른 박스에 의해 다시 굽어지지 않습니다. Limited/Unlimited는 박스 높이 안에서 같은 굽힘이므로 윤곽선도 같습니다. 이동·회전·스케일은 변환된 원래 박스에서 표시를 다시 계산하고, 취소하면 즉시 원래 표시로 돌아옵니다. 표시용 곡선을 Rhino 문서나 Bake/Merge 결과에 추가하지 않습니다.

내부 편집의 일반 클릭은 휘어진 선을 대상으로 선택하며 검볼은 원래 박스의 기준을 사용합니다. 명령 실행 중의 객체 선택, Shift/Ctrl·창 선택, 편집 중 스냅은 Rhino의 원래 직교 박스 기준입니다. **Show result OFF**에서는 원래 박스를 표시합니다. 비직교 변형 등으로 박스가 손상되면 복구할 수 있도록 원래 선을 표시합니다.

곡률 중심을 넘어 형상이 붕괴하는 Strength는 기존 값을 유지하며 거부합니다. Unlimited에서 실제 입력이 한 바퀴 이상 감기는 경우도 제한합니다. 박스가 비직교 형태로 전단되거나 손상되면 오류를 표시하므로 Undo하거나 새 박스로 교체합니다. 회전된 박스의 비균등 크기를 조절할 때는 로컬 축을 사용합니다. [0.15.1 화면 확인 순서](MD/Bend_Smoke_Test.md), [계산 방식 검토](MD/Bend_Feasibility_Study.md).

## 하위 트리 복제

Modifier 한 행을 선택하고 **Duplicate subtree**를 누르면 선택한 Modifier와 모든 하위 트리를 **같은 부모의 바로 다음 위치**에 복제합니다. 이름·종류·순서·ON/OFF·속성·입력 선 표시를 유지하며, 각 원본은 별도의 GUID를 가진 실제 Rhino 객체로 복제합니다. 복제본을 편집해도 기존 트리의 원본은 변하지 않습니다.

Rhino 원본 이름·레이어 등 기본 객체 속성은 유지하지만 기존 Rhino 그룹에는 복제본을 추가하지 않습니다. 복제본 자체의 잠금·숨김은 해제하며, 원본 레이어가 잠겨 있거나 숨겨져 있으면 복제본을 현재 레이어에 둡니다. 이름이 같은 두 Modifier는 속성의 Name으로 구분할 수 있습니다. 한 번의 Undo는 복제 트리와 이번에 만든 모든 원본 객체를 함께 제거하고, Redo는 함께 복원합니다. 여러 행을 선택한 상태에서는 복제 버튼을 비활성화합니다.

## Mirror와 직선·격자 Array

두 종류 모두 하나 이상의 닫힌 Brep/Extrusion 입력 또는 하위 Modifier 결과를 받습니다. 여러 자식이 있으면 각 자식의 모든 결과 조각에 같은 변환을 적용합니다.

| 종류 | 속성 | 결과 |
|---|---|---|
| Mirror | Set Plane, Keep original, Union | 편집 가능한 평면을 기준으로 대칭 생성; 기본 합집합 |
| Array | Count/Spacing X/Y/Z, Set Axis, Reset Axis | 축별로 지정한 방향으로 입력 전체를 반복 배치 |

새 Mirror에는 월드 원점을 지나는 YZ 평면의 **BasePlane**이 생깁니다. **Set Plane**에서 원점·X 방향점·평면 위 세 번째 점을 찍으면 기준 평면을 바꿉니다. 취소하거나 중복점·일직선 점을 지정하면 기존 평면을 유지합니다. 화면의 사각형은 조작용이며 실제 대칭 계산에는 무한 평면을 사용합니다. BasePlane은 순서와 무관한 전용 제어 객체로, 입력 수·Boolean·대칭 결과에서 제외됩니다.

Mirror 전체를 선택하면 원본과 BasePlane이 함께 이동·회전합니다. 더블클릭하여 내부에 들어가면 BasePlane만 선택해 Move/Rotate/Gumball로 편집할 수 있습니다. BasePlane은 소유한 Mirror 밖으로 드래그하거나 단독으로 제거할 수 없습니다. Mirror를 제거하면 일반 자식은 승격하고 제어 평면은 같은 위치에 숨깁니다. 하위 트리 복제는 평면까지 독립적으로 복제합니다.

새 Mirror의 **Keep original / Union은 기본 ON**입니다. Union은 원본과 대칭 결과를 합치며, 떨어진 솔리드는 여러 조각으로 유지합니다. Union OFF는 별도 조각을 유지하고, Keep original OFF는 대칭 결과만 남깁니다. 합집합 실패는 오류 상태로 표시합니다. 0.12.0 파일을 읽을 때는 기존 결과를 보존하기 위해 Union OFF와 수치 평면을 유지합니다. 기존 Mirror에서도 Set Plane을 한 번 실행하면 편집 가능한 BasePlane을 만듭니다.

Array는 기본값 Count `(2, 1, 1)`, Spacing `(10, 10, 10)`이며 간격은 문서 단위입니다. Count는 **원본 위치를 포함**하고, 사용하지 않는 축은 1로 둡니다. 예를 들어 Count `(3, 2, 1)`과 Spacing `(20, 15, 0)`은 6개 위치에 배치합니다. 음수 간격도 사용할 수 있습니다. 각 Count는 양의 정수이고 곱은 최대 **256개 위치**이며, 실행 시 결과 조각 총수도 **4,096개**로 제한합니다.

축 기본값은 월드 X/Y/Z입니다. 각 **Set Axis**에서 찍은 첫 점→둘째 점의 방향만 저장하므로 두 점 사이 거리는 Spacing이나 배열 시작 위치에 영향을 주지 않습니다. 축은 서로 직교하지 않아도 되며 **Reset Axis**는 해당 축만 월드 방향으로 돌립니다. 별도 축 객체는 만들지 않습니다. Array 전체나 상위 Modifier를 회전하면 저장된 축도 함께 회전하고, 내부 원본만 회전하면 배열 축은 유지됩니다. 전체 확대·축소에서는 축 방향과 간격에 변환을 반영합니다. 이동 중 미리보기와 확정 결과에 같은 계산을 사용하며, 원본 변환과 축 설정은 같은 Undo 기록에 들어갑니다.

대칭·배열된 작업용 결과는 입력 편집에 따라 갱신합니다. 고정 객체는 Bake/Merge로 만듭니다. Array와 Union OFF인 Mirror는 겹치는 복사본을 자동으로 합치지 않으며, 상위 Boolean에 넣으면 그 입력 조각들의 합집합을 기준으로 연산합니다. Bake/Merge 자체는 여러 결과 조각을 한 Brep에 담습니다. **BasePlane은 Bake/Merge 결과에 포함되지 않고, 위치·회전·크기도 변하지 않습니다.** Bake는 원래 트리와 평면을 유지합니다. Merge는 평면을 같은 위치에 숨기고 트리에서 제거하며 Undo로 복원합니다. 고정 결과를 움직여도 기존 평면은 따라가지 않습니다.

[0.13.0 Mirror·Array 확인 순서](MD/Mirror_Array_Usability_Smoke_Test.md)를 참고하세요.

## Modifier 이름과 Bake / Merge

Modifier의 **Name 셀을 더블클릭**하거나 속성의 **Name → Enter**로 이름을 편집합니다. `Main`을 입력하면 종류에 따라 `(BD) Main`, `(BU) Main`, `(BI) Main`, `(Mirror) Main`, `(Array) Main`으로 표시합니다. 편집 중에는 종류 접두사를 제외한 이름만 보이며, 이름을 비우면 기본 종류명으로 돌아갑니다. 이름만 바꿀 때는 결과와 선택을 유지합니다. 이름 변경도 Undo/Redo와 `.3dm` 저장 대상입니다.

현재 유효하고 비어 있지 않은 결과가 있는 Modifier를 선택하면 **Bake / Merge** 버튼이 활성화됩니다.

| 작업 | 트리 변화 | Rhino 객체 |
|---|---|---|
| Bake | 기존 Modifier를 유지하고 결과를 루트 맨 위에 추가 | 사용자 이름의 고정 결과 하나 생성 |
| Merge | 선택한 Modifier와 하위 트리를 결과 하나로 교체, 부모와 형제 순서 유지 | 결과 하나 생성, 기존 하위 원본은 숨겨 보관 |

`(BD) Main`의 고정 결과 이름은 `Main`입니다. 결과가 여러 조각이어도 형태와 빈 공간을 유지한 하나의 Brep 객체로 다룹니다. 서로 떨어진 조각 사이를 채우거나 연결하지 않습니다. 고정 결과는 원본을 움직여도 변하지 않으며 다시 다른 Modifier의 입력으로 쓸 수 있습니다.

Merge는 원본을 Rhino 문서에 숨겨 남깁니다. Undo 한 번으로 결과를 제거하고 원래 트리와 원본 표시 상태를 복원합니다. Bake의 Undo는 이번에 생성한 결과와 등록만 제거합니다. 미리보기에서 이전 결과를 유지 중인 오류 상태나 Empty 결과는 생성하지 않습니다. Merge는 잠긴 원본·참조 원본·제어점 편집 중인 원본을 먼저 수정 가능한 상태로 바꿔야 합니다.

0.14.0에서는 Bake/Merge를 시작하기 전에 작업용 결과와 임시 숨김을 정리하여 원본의 표시 상태를 기준으로 작업과 Undo를 기록합니다. 이후 현재 트리에 맞게 작업용 결과를 다시 만듭니다. 이 과정에서도 BasePlane 형상을 이동·회전·확대하지 않습니다.

[0.10.0 이름·Bake·Merge 확인 순서](MD/Bake_Merge_Smoke_Test.md)와 [트리 변화 예시](MD/Bake_Merge_Naming_Spec.md)를 참고하세요.

## 저장·복원과 Undo/Redo

Rhino의 일반 **Save / Save As**로 `.3dm`을 저장하면 원본 객체 GUID, Modifier 종류와 노드 ID, 이름, 부모·자식 관계, 루트와 자식 순서, **Enabled / Mirror·Array 속성 / InputWire / Show result** 설정을 함께 기록합니다. 파일을 열면 원본 GUID를 다시 연결하고 읽기가 끝난 뒤 미리보기를 재계산합니다. 별도 관리 파일은 필요하지 않습니다. Enter/Apply로 확정하기 전의 속성 입력값은 저장하지 않습니다.

작업용 결과는 `.3dm`의 영구 객체로 저장하지 않습니다. 일반 문서 저장의 `BeginSaveDocument`에서 원본의 기존 표시 모드를 복구하고 작업용 객체를 제외한 뒤, 저장이 끝난 다음 Idle에서 다시 만듭니다. 따라서 작업용 숨김이 원본의 영구 Hide로 굳어지지 않으며 사용자가 원래 숨기거나 잠근 객체의 상태는 유지합니다. 트리 저장 형식은 0.13.0과 같은 **schema 4**입니다. 파일을 다시 열면 작업용 결과를 새로 계산하므로 중복된 결과 객체가 쌓이지 않아야 합니다.

0.8.0까지의 트리는 저장되지 않았으므로 0.9.0 설치 후 한 번 구성해 저장해야 합니다. 미리보기 형상, 현재 선택·내부 편집 범위, 행 접힘 상태, Undo 기록은 저장하지 않습니다. 원본 이름은 기존 Rhino 객체 속성으로 저장됩니다.

원본 등록, Modifier 추가·복제, 속성 Enter/Apply, Enabled, 드래그 순서·부모 변경, 루트로 꺼내기, Remove, InputWire와 Show result 변경을 Rhino의 **Undo / Redo**에 연결했습니다. 한 번에 등록한 여러 객체는 한 번의 Undo로 해제됩니다. 원본 Move·이름 변경 등 기존 Rhino 작업과 같은 기록을 사용합니다. 변경 없는 드롭이나 중복 등록은 기록을 추가하지 않으며, 실제 트리 편집은 문서를 수정된 상태로 표시합니다.

삭제된 원본의 등록 정보는 `Missing`으로 유지합니다. 다른 문서로의 Import, Insert, 복사·붙여넣기는 트리를 가져오지 않습니다. Export Selected와 사용자 데이터 저장을 끈 저장에서는 트리를 기록하지 않습니다. 지원하지 않는 형식이나 손상된 저장 데이터는 명령 기록에 알리고 트리 편집을 막으며, 읽어낼 수 있는 원본 데이터는 다음 저장에도 보존합니다.

[0.9.0 저장·복원 및 Undo/Redo 확인 순서](MD/Persistence_Undo_Smoke_Test.md)를 참고하세요. 실제 Rhino에서 플러그인 자동 로드와 연속 Undo/Redo를 확인하는 절차도 포함되어 있습니다.

## 미리보기와 원본 편집

0.14.0의 최종 결과는 문서에 존재하는 **작업용 Brep**으로 Rhino의 기본 표시·선택·오브젝트 스냅을 사용합니다. 평소에는 트리 내부 원본과 BasePlane에 실제 Hide를 적용하므로, 결과에 없는 원본 모서리나 점이 스냅에 끼어들지 않습니다. 등록하지 않은 객체와 트리 최상위의 일반 원본은 이 숨김 대상에서 제외합니다.

| 작업 상태 | 선택·스냅할 수 있는 대상 |
|---|---|
| 일반 작업 | 최종 결과와 원래 보이던 일반 Rhino 객체 |
| Modifier 내부 편집 | 최종 결과, 현재 단계의 원본·BasePlane, 현재 단계의 자식 Modifier 결과 |
| InputWire만 ON | 선택·스냅 범위는 그대로 유지; 해당 입력의 참고 선만 추가 |
| Show result OFF | 작업용 결과 제거, 원본의 기존 표시·잠금 상태 복구 |

원본에 사용자가 직접 지정한 Hide·잠금은 보존하며, 내부 편집을 연다는 이유로 해제하지 않습니다. 더 깊은 단계의 원본은 그 단계에 들어갈 때까지 숨긴 상태를 유지합니다. [0.14.0 스냅·선택·저장 확인 순서](MD/Working_Result_Smoke_Test.md)에 따라 실제 마우스 조작을 확인합니다.

Boolean 및 Mirror/Array 입력은 닫힌 Brep/Extrusion이어야 합니다. Curve는 등록할 수 있지만 현재 Modifier 계산에는 사용할 수 없습니다.
ON인 Boolean은 입력이 두 개 이상, Mirror/Array 및 OFF인 Modifier는 입력이 하나 이상 필요합니다. 입력이 부족하면 `Needs inputs`이며 결과를 표시하지 않습니다. OFF 행의 State에는 ON/OFF 구분을 위해 `OFF`를 표시합니다.

| 종류 | 계산 | 빈 자식 결과 |
|---|---|---|
| Difference (BD) | 첫 입력에서 나머지를 빼기 | 첫 입력이 비면 Empty, 빈 Cutter는 건너뜀 |
| Union (BU) | 모든 입력 합치기 | 빈 입력은 건너뜀, 전부 비면 Empty |
| Intersection (BI) | 모든 입력에 공통인 부분 | 하나라도 비면 Empty |

세 종류를 서로 중첩할 수 있습니다. Intersection의 한 자식이 여러 조각을 반환하면 그 조각 전체를 한 입력 집합으로 계산합니다. Union/Intersection의 입력은 동등한 역할이며, 상위 Difference의 Cutter 아래에 있으면 그 하위 입력 전체가 Cutter로 표시됩니다.

원본 행을 선택하고 **Select in Rhino**를 누르면 그 원본의 부모 편집 단계로 이동하여 원본을 노출하고 선택합니다. 이름 더블클릭은 선택 명령 대신 이름 편집을 실행합니다.
Move, Rotate, Scale1D, Gumball 등으로 편집한 뒤 명령이 끝나면 결과를 재계산합니다.
현재 편집 단계의 입력은 모서리로 표시하며, 이동 중에는 현재 임시 위치로 Boolean 결과도 갱신합니다. `MTreeMove`의 위치 또는 Rhino의 동적 변환값을 복제된 입력에 적용해 상위 Modifier까지 평가합니다. 드래그 중 계산은 원본 형상을 변경하지 않으며, 확정할 때 원본과 필요한 패턴 설정을 함께 기록합니다.
계산 시작 간격은 33ms를 목표로 하며, 계산 완료 후 80ms를 추가 대기하던 처리를 제거했습니다. UI 타이머는 16ms 간격으로 확인하므로 실제 간격에는 타이머 오차와 그리기 비용이 포함됩니다. 같은 위치에서는 재계산을 생략합니다. 전체 평행이동은 재사용 가능한 확정 결과의 복사본에 이동을 적용하고, 개별 이동은 변경된 원본에서 상위로 이어지는 경로만 평가합니다. 중첩 Modifier 전체 이동도 재사용 가능한 내부 결과를 사용하되 그 부모는 다시 계산합니다. 월드 평면에 고정된 Mirror가 영향을 받는 경로는 이동 중에도 다시 평가합니다. 회전·Scale은 일반 평가를 사용합니다.
취소 시 임시 캐시를 버리고 기존 결과로 돌아오며, 확정·Undo·원본 변경·트리 변경·허용오차 변경 후 캐시를 새로 구성합니다. `MTreeStatus`에서 마지막 실시간 계산 시간, 계산한 Boolean Modifier 수, 평가 노드 수와 재사용 Modifier 수를 확인합니다. 입력이 세 개인 Intersection처럼 하나의 Modifier에서 여러 네이티브 연산을 실행할 수 있습니다. [0.8.0 성능 비교와 화면 확인 순서](MD/Preview_Performance_0.8.0.md)를 참고하세요.

- 정상 결과는 첫 번째 기하 입력의 레이어·색상·재질 등 기본 객체 속성을 받아 Rhino의 기본 표시 경로로 그립니다. 이동 중 결과와 편집 단계의 자식 모서리는 기존 Conduit 표시를 함께 사용합니다.
- 입력 선은 A와 Cutter 모두 기본 숨김입니다. **InputWire 셀 더블클릭** 또는 **Show input wires**로 개별 표시를 켭니다. Rhino 또는 패널에서 선택한 입력은 임시로 모서리를 표시하며 지속 표시 설정을 바꾸지는 않습니다.
- 표시한 첫 입력의 선은 회색, Cutter는 주황색, 선택된 입력은 노란색입니다. **Show result**를 끄면 원본 일반 표시로 돌아갑니다.
- `Empty`는 정상적으로 계산했지만 남은 형상이 없는 상태입니다.
- 원본 편집으로 계산이 실패하면 마지막 정상 결과를 주황색 참고 표시로 남깁니다. 유효한 현재 결과의 스냅 객체는 제거하며, 원본 행에서 **Select in Rhino**를 눌러 해당 편집 단계에서 문제를 수정합니다. 원본을 복구하면 다시 계산합니다.
- 트리 구조를 바꿀 때는 이전 구조의 결과 캐시를 지워 오래된 수식의 결과가 남지 않도록 합니다.

결과 클릭은 그 Modifier의 작업용 Brep 선택으로 연결됩니다. 자동 갱신은 이 Brep을 교체하고 원본의 임시 숨김을 관리합니다. 원본 편집의 Undo/Redo 후에는 현재 문서와 트리로 다시 계산합니다. 작업용 갱신 자체는 별도 Undo 작업이나 문서 수정 표시를 추가하지 않습니다.

작업용 결과는 **전체 이동·회전·크기 변경과 스냅 기준**으로 사용합니다. 결과에 직접 Trim·Boolean·면/모서리 편집 같은 형상 변경을 적용하면 트리 재계산으로 덮어써질 수 있으므로, 해당 작업을 하려면 먼저 **Bake 또는 Merge**로 고정합니다. 일반 Rhino **Copy**로 작업용 결과를 복사하면 트리와 연결되지 않은 독립된 고정 Brep이 생깁니다. 이 복사본은 자동으로 트리에 등록하지 않으며, 필요하면 **Add Object**로 등록합니다. 트리 구조까지 독립적으로 복제하려면 **Duplicate subtree**를 사용합니다.

0.14.1에서는 작업용 객체의 GUID뿐 아니라 생성·교체 전후의 native runtime serial도 추적합니다. Rhino Undo가 이전 객체에 새 GUID를 부여해 복원해도 해당 Modifier의 임시 결과로 인식하여 하나로 정리하며, 일반 Copy는 별도 serial이므로 보존합니다. 생성 API가 다른 GUID를 반환하는 경우에도 실제 생성된 객체를 관리합니다. 0.14.0에서 이미 유출된 중복을 저장한 파일은 과거 세션의 소유 이력이 없으므로 자동 삭제하지 않습니다. [중복 재현·수정 확인](MD/Working_Result_Duplicate_Fix.md)을 참고합니다.

정지 상태의 결과는 Rhino 기본 렌더 경로를 사용하지만, 드래그 중 표시·텍스처 매핑·Raytraced 등 각 표시 모드의 세부 동작은 실제 뷰포트에서 확인해야 합니다. [이전 Conduit 표시 방식 검토](MD/Preview_Display_Feasibility.md)는 0.14.0 이전 구조를 설명하는 참고 문서입니다.

## 전체·부분 트리 이동

1. 패널에서 Modifier 또는 원본 행을 선택하고 명령창에서 **MTreeMove**를 실행합니다. 뷰포트에서 선택한 결과나 입력은 Rhino Move와 검볼로도 움직일 수 있습니다.
2. 기준점과 도착점을 지정합니다. 마우스 이동 중 노란 선으로 이동 위치를 확인할 수 있습니다.
3. 최상위 Modifier는 모든 하위 원본, 중간 Modifier는 해당 하위 원본만 이동합니다. 원본 행은 해당 객체만 이동합니다.
4. 명령 완료 후 최상위 결과를 다시 계산합니다. Undo 한 번으로 이 명령의 원본 이동 전체를 되돌립니다.

이동 확정 전 Esc는 원본을 변경하지 않습니다. 누락·잠금·사용자가 지정한 Hide·참조 객체 또는 제어점이 켜진 원본이 있으면 전체 이동을 시작하지 않습니다. 작업용 표시를 위해 플러그인이 임시로 숨긴 원본은 노출하지 않고 변환할 수 있습니다.
뷰포트에서 결과를 한 번 클릭하거나 패널의 Modifier 행에서 **Select in Rhino**를 누르면 해당 작업용 결과를 선택합니다. Rhino Move·Rotate·Scale·Gumball을 적용하면 모든 하위 원본과 BasePlane, Array 축·간격에 같은 변환을 전달합니다. 변환한 형상과 패턴 설정은 한 번의 Undo로 함께 복구합니다. 여러 독립 결과를 Rhino에서 함께 선택해 변환할 수도 있습니다.

## 뷰포트 선택과 내부 편집

- 결과 한 번 클릭: 최상위 Modifier의 작업용 결과 선택. 하위 원본은 숨긴 상태를 유지하며 결과를 움직이면 함께 변환됩니다.
- 결과 더블클릭: 해당 Modifier 내부로 진입. 패널 State는 `Editing`, 바로 아래 원본과 BasePlane은 편집 가능 상태로 복구하고 모서리로 표시합니다. 자식 Modifier에는 그 단계의 작업용 결과가 생기며 더 깊은 원본은 숨깁니다.
- 내부에서 모서리 클릭: 그 자식만 선택. 원본이면 개별 편집, 자식 Modifier이면 그 작업용 결과를 통해 아래 원본들을 함께 조작합니다. 현재 단계의 입력·자식 결과와 최종 결과에 스냅할 수 있습니다.
- 자식 Modifier 모서리 더블클릭: 한 단계 더 들어갑니다.
- 명령이 실행 중이지 않을 때 Esc 또는 빈 공간 더블클릭: 한 단계 나와서 방금 편집한 Modifier를 선택합니다. 이동 중 Esc는 먼저 이동을 취소합니다.
- 내부 편집용 선 표시는 InputWire의 저장된 ON/OFF 값을 바꾸지 않습니다. 뷰포트 선택은 패널 행에도 반영됩니다. 편집 단계를 나가면 해당 원본을 다시 숨겨 일반 작업의 스냅 범위로 돌아옵니다.
- 뷰포트 선택 또는 패널의 Select in Rhino 후 기본 검볼을 자동 활성화합니다. 전체 Modifier와 개별 원본 모두 적용됩니다. 마우스 처리가 끝난 뒤 명령 대기 상태에서 한 번 갱신하며, 그 전에 선택을 해제하거나 내부로 진입하면 이전 선택의 갱신은 취소합니다.

위 규칙은 명령 대기 중 일반 왼쪽 클릭에 적용합니다. Shift/Ctrl 조합, 창 선택, 명령의 객체 선택 단계와 Gumball 핸들은 Rhino 기본 처리를 유지합니다. **Show result**가 꺼져 있으면 일반 Rhino 선택을 사용합니다. 트리 구조를 바꾸면 편집 범위는 최상위로 돌아갑니다.
Wireframe에서는 결과 모서리를 클릭합니다. 결과가 `Empty`이거나 입력이 부족해 보이지 않으면 패널에서 **원본 행**을 선택한 뒤 **Select in Rhino** 또는 **MTreeMove** 명령으로 편집합니다.
실제 더블클릭·Gumball·Esc·스냅 동작은 [0.14.0 작업용 결과 확인 순서](MD/Working_Result_Smoke_Test.md)를 따라 확인합니다. [0.7.1 화면 확인 순서](MD/Viewport_Selection_Smoke_Test.md)는 이전 선택 구조의 기록입니다.

## 명령

| 명령 | 기능 |
|---|---|
| `MTree` | Modifier Manager 열기 |
| `MTreeAddObjects` | Brep / Extrusion / Curve 원본 등록 |
| `MTreeDifference` | 빈 Boolean Difference를 최상위에 생성 |
| `MTreeUnion` | 빈 Boolean Union을 최상위에 생성 |
| `MTreeIntersection` | 빈 Boolean Intersection을 최상위에 생성 |
| `MTreeMirror` | BasePlane을 가진 Mirror를 최상위에 생성 |
| `MTreeArray` | 빈 Array를 최상위에 생성 |
| `MTreeBend` | 빈 Bend를 최상위에 생성 |
| `MTreeAddControlBox` | 선택한 Bend에 입력 크기에 맞는 Control Box 추가 |
| `MTreeFitControlBox` | 선택한 Control Box를 현재 방향으로 입력에 다시 맞춤 |
| `MTreeSetPlane` | 선택한 Mirror의 평면을 세 점으로 설정 |
| `MTreeSetAxis` | 선택한 Array의 X/Y/Z 축을 두 점으로 설정 |
| `MTreeDuplicate` | 선택한 Modifier와 하위 트리·원본 객체를 독립적으로 복제 |
| `MTreeMove` | 패널에서 선택한 노드와 모든 하위 원본 이동 |
| `MTreeBake` | 선택한 Modifier의 고정 결과를 루트에 추가 |
| `MTreeMerge` | 선택한 Modifier와 하위 트리를 고정 결과로 교체 |
| `MTreeRebuild` | 전체 Modifier 트리 재계산 예약 |
| `MTreeStatus` | 버전, 등록 수, 트리 및 계산 상태 확인 |
| `MTreeTrace` | 현재 문서 이벤트 기록 ON/OFF |
| `MTreeDumpLog` | 최근 최대 2,000개 이벤트를 명령 기록에 출력 |

Transform 진단은 `MTreeTrace`를 켜고 조작한 뒤 끄고 `MTreeDumpLog`로 확인합니다.
Rhino 원본만 복사할 때는 별도 `Copy` 명령을 사용하며, Modifier 하위 구조 전체의 독립 복제는 `MTreeDuplicate`를 사용합니다. 사용자 환경의 Move에는 `Copy=Yes` 옵션이 없었습니다.
진단 로그는 메모리에만 유지되며 문서를 닫으면 사라집니다.

## 검증 현황

2026-09-11 Release **0.15.2: Core 205 + Rhino·Eto/WPF 772 = 977 PASS**, 빌드 경고·오류 0개, 기존 headless Undo/Redo 제약 3 SKIP. 기존 Rhino 전체·하위 객체 선택, persistent 선택, Eto 다중 행 선택 및 전체 회귀 검사를 통과했습니다. 새 패널 자동 선택부터 검볼 표시까지의 실제 마우스 동작은 [패널 선택 확인 순서](MD/Panel_Selection_Smoke_Test.md)의 수동 확인 대상입니다. [빌드 로그](artifacts/validation/0.15.2/release-build-2026-09-11.txt), [전체 검사 로그](artifacts/validation/0.15.2/rhino-checks-2026-09-11.txt).

2026-09-11 Release **0.15.1: Core 205 + Rhino·Eto/WPF 772 = 977 PASS**, 빌드 경고·오류 0개. 굽힌 cage의 ±180°·0°·중립축 길이·곡선 선택, 이동·회전·비균등 스케일 재계산, 캐시 재사용·취소·손상 복원·폐기를 포함합니다. 전체 회귀 검사와 최종 빌드 후 재검사 모두 통과했습니다. 기존 창 없는 호스트의 연속 Undo/Redo 제약 3 SKIP은 유지합니다. 실제 마우스·검볼·Show result 전환은 [화면 확인 순서](MD/Bend_Smoke_Test.md)의 수동 검증 대상입니다. [최종 빌드 로그](artifacts/validation/0.15.1/release-build-2026-09-11.txt), [전체 Rhino 검사 로그](artifacts/validation/0.15.1/rhino-checks-2026-09-11.txt), [최종 재검사 로그 발췌](artifacts/validation/0.15.1/rhino-recheck-excerpt-2026-09-11.txt). 재검사 로그는 출력 크기 제한으로 일부 생략되어 있습니다.

이전 Release **0.15.0: Core 205 + Rhino·Eto/WPF 719 = 924 PASS**. Control Box 축의 native 변환 및 실제 3dm 왕복, 순차 Bend·Boolean·마지막 유효 결과, Add/Fit/속성 변경의 원자성·잠금·Undo, 복제·Bake/Merge·제거의 제어 객체 보존, 원본 숨김·작업용 결과·실시간 변형 캐시와 UI Enter 입력을 확인했습니다. 기존 Boolean·Mirror·Array 및 결과 중복 회귀 검사도 포함합니다. [빌드 로그](artifacts/validation/0.15.0/release-build-2026-09-11.txt), [전체 Rhino 검사 로그](artifacts/validation/0.15.0/rhino-checks-2026-09-11.txt). Bend만 확인하려면 `scripts/check-rhino.ps1 -Configuration Release -BendOnly`를 사용합니다.

2026-09-11 Release 0.14.2: 빌드 경고·오류 0개, **Core 172개 + Eto/WPF UI 49개 통과**. 180/300/500/760px 패널에서 Bake·Merge·Remove의 같은 줄·균등 너비·패널 내 배치와 실제 Eto KeyDown 이벤트를 통한 Array 여섯 입력칸의 Enter 제출·입력 검증·키 소비·Tab 이동을 확인했습니다. [빌드 로그](artifacts/validation/0.14.2/release-build-2026-09-11.txt), [UI 검사 로그](artifacts/validation/0.14.2/ui-checks-2026-09-11.txt). 전체 Rhino 형상 검사는 260개 통과 후 검증 호스트의 `Rhino.Runtime.NotLicensedException`으로 중단되어 완료로 집계하지 않습니다. [인증 오류 로그](artifacts/validation/0.14.2/rhino-checks-license-error-2026-09-11.txt). UI 검사는 `scripts/check-rhino.ps1 -Configuration Release -UIOnly`로 RhinoCore 없이 별도 실행할 수 있습니다.

2026-09-11 Release 0.14.1: **Core 172 + Rhino 608 = 780 PASS**, 기존 호스트 제약 3 SKIP, 빌드 경고·오류 0개. 이동 뒤 결과 교체와 Undo에서 두 객체가 남는 현상을 native API로 재현했고, 새 GUID로 복원된 runtime serial 추적·중복 정리·Copy 보존 관련 회귀 검사 17개를 추가했습니다. 실제 UI의 연속 Undo/Redo는 설치 후 확인 대상입니다. [0.14.1 수정·검증 기록](MD/Working_Result_Duplicate_Fix.md)에 로그와 확인 순서를 정리했습니다.

2026-09-11 Release 0.14.0: **Core 172개 + Rhino 591개 = 763개 검사 통과**, 빌드 경고·오류 0개입니다. [Release 로그](artifacts/validation/0.14.0/release-build-2026-09-11.txt)와 [Rhino 검사 로그](artifacts/validation/0.14.0/rhino-checks-2026-09-11.txt)를 보관합니다. 별도 Rhino 네이티브 명령 검사 26개와 전체 DocumentSession 연동 검사 35개도 통과했습니다. 이 61개는 실제 Move·Rotate·Undo/Redo, 저장 훅, Copy, Merge와 편집 범위 연동을 확인한 기록이며, 마지막 저장 보호·Hide 선택 보완 후 재실행한 주 검사 763개와 별도로 집계합니다. 기존 화면 없는 호스트의 연속 Undo/Redo 제한에 따른 3개 SKIP은 유지합니다. 실제 마우스의 End·Near·Cen 스냅, 검볼 표시·드래그, 더블클릭 편집 단계와 파일 재시작은 [0.14.0 확인 순서](MD/Working_Result_Smoke_Test.md)의 수동 확인 대상입니다.

검증용 Rhino는 `/scheme=ModifierTreeValidation014`로 별도 설정 환경을 사용합니다. 작업 중 보고된 `LoadPlugIn cannot be called while a plug-in is being loaded` 알림은 초기 검증 호스트가 기본 설정 환경을 공유하며 패널을 복원한 것이 유력한 원인이나, 정확한 중첩 로드 경로는 확인되지 않았습니다. 알림 이후 GUI 호스트는 다시 실행하지 않았고 최종 검사는 `WindowStyle.NoWindow`로 완료했습니다. 이 관찰만으로 제품의 플러그인 초기화 코드를 변경하거나 기존 설치·설정을 초기화하지 않았습니다. 관련 [검증 기록](artifacts/probes/WorkingProxy014/Findings.md)을 참고합니다.

2026-09-11 Release 0.13.0: 빌드 오류·경고 0개, Core 172개와 Rhino 490개, **총 662개 검사 통과**. BasePlane 소유권·저장·평면 재설정·복제·Bake/Merge 후 위치 보존, Array 방향·전체/개별 회전 구분·이동 중 캐시·한 번의 Undo, 좁은 패널의 새 버튼을 검사했습니다. 화면 없는 호스트가 지원하지 않는 연속 Undo/Redo 3개 항목은 수동 확인으로 남겼습니다. 실제 마우스와 검볼의 이벤트 전달·표시는 [0.13.0 확인 순서](MD/Mirror_Array_Usability_Smoke_Test.md)를 사용합니다. [빌드 기록](artifacts/validation/0.13.0/release-build-2026-09-11.txt)과 [Rhino 검사 기록](artifacts/validation/0.13.0/rhino-checks-2026-09-11.txt)을 보관했습니다.

2026-09-11 Release 0.12.0: 빌드 오류·경고 0개, Core 141개와 Rhino 411개, **총 552개 검사 통과**. 화면 없는 호스트에서 지원하지 않는 연속 Undo/Redo 관련 3개 항목은 수동 확인으로 남겼습니다. [빌드 기록](artifacts/validation/0.12.0/release-build-2026-09-11.txt)과 [Rhino 검사 기록](artifacts/validation/0.12.0/rhino-checks-2026-09-11.txt)을 보관했습니다. 실제 키·마우스 조작, 플러그인 자동 로드와 연속 Undo/Redo는 [0.12.0 확인 순서](MD/Modifier_Properties_Patterns_Smoke_Test.md)의 수동 확인 대상입니다.

2026-09-10 Release 0.11.0: 빌드 오류·경고 0개, Core/GUID·이름·저장 상태·묶음 편집 검사 103개, Rhino 엔진·문서·선택·최적화·아카이브·Undo·Bake/Merge·Eto/WPF 검사 293개 통과(총 396개). 화면 없는 호스트에서 실행할 수 없는 연속 Undo/Redo 관련 3개 항목은 수동 확인으로 남겼습니다. [빌드 기록](artifacts/validation/0.11.0/release-build-2026-09-10.txt)과 [Rhino 검사 기록](artifacts/validation/0.11.0/rhino-checks-2026-09-10.txt)을 보관했습니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-rhino.ps1 -Configuration Release
```

검사에는 빈 Modifier, 순서 반전, 여러 Cutter, 재배치·삭제, 순환 방지, 중첩 계산, 여러 결과 조각 전달, 오류 복구, 전체·부분·개별 Move와 Undo, 이동 전 유효성 검사, 입력 표시와 재질 규칙이 포함됩니다. 0.11.0에서는 다중 선택의 순서 유지·부모 변경·전체 거부·단일 Undo, Eto/WPF 선택 복원, 세 Boolean 종류의 저장 상태, Union/Intersection의 부피·조각 수·중첩·이동 캐시·Bake/Merge를 추가로 검사합니다.
저장 검사는 네이티브 바이너리 아카이브 왕복과 실제 `.3dm`의 형상·동일 저장 데이터 왕복 후 GUID 연결 및 Boolean 재계산을 포함합니다. `.3dm` 검사에서는 객체 사용자 사전에 저장한 동일 아카이브를 사용하므로 플러그인의 파일 읽기·쓰기 훅과 자동 로드 전체를 검증한 것은 아닙니다.
화면 없는 Rhino 검사 호스트에서는 문서 스냅샷에서 재계산을 직접 호출합니다. 이 호스트의 `RhinoDoc.Redo()`와 연속 두 번째 Undo는 플러그인 없는 기준 검사에서도 실패했습니다. 0.9.0 저장·복원과 트리 편집, 0.10.0 이름·Bake/Merge의 기본 동작은 사용자 확인을 받았습니다. 2026-09-11에는 0.11.0 업데이트 후 정상 동작한다는 사용자 확인을 받았습니다. 각 세부 조작의 재검사는 [다중 선택과 Boolean 확인 순서](MD/Multi_Select_Boolean_Smoke_Test.md)를 사용합니다.
여러 솔리드를 하나의 Brep으로 저장한 뒤 상위 Boolean에 넣을 때 일부가 빠지던 문제는 조각을 나누어 전달하여 처리합니다. 기존 내부 공동의 방향은 보존합니다. 다만 완전히 포함된 Cutter로 새로운 공동을 만드는 기존 Boolean 경로는 검사 호스트에서 예상과 다른 결과가 관찰되어 별도 확인 과제로 남겼습니다.
0.4.0까지 기본 화면 동작은 사용자 확인을 받았습니다. 0.5.0은 [새 UI·Cutter 화면 검증 순서](MD/UI_Cutter_Smoke_Test.md)로 확인합니다. 실제 Eto/WPF 레이아웃을 창 없이 180/300/500/760 너비에서 측정했으며 버튼의 세로 배치와 너비, 트리·열의 크기를 검사했습니다. 실제 마우스 더블클릭과 동적 뷰포트 표시는 아직 수동 확인 대상입니다.

## 구조와 현재 제한

```text
src/ModifierTree.Core/           순서와 부모·자식 관계를 관리하는 트리 모델
src/ModifierTree.Rhino/          플러그인 진입점과 문서별 세션
  Commands/                     Rhino 명령
  Diagnostics/                  이벤트 기록
  Modifiers/                    재귀 평가와 Boolean 계산·캐시
  Persistence/                  문서 아카이브와 트리 Undo 기록
  Display/                      결과 미리보기와 입력 선
  UI/                           Eto 트리 패널과 드래그 편집
tests/ModifierTree.Core.Checks/  트리·저장 상태·대기·GUID 검사
tests/ModifierTree.Rhino.Checks/ 실제 Rhino 엔진 검사
scripts/                        SDK 준비와 빌드
MD/                             구상과 수동 검증 문서
```

노드 ID와 원본 Rhino 객체 GUID는 별도로 관리합니다. 문서별 세션은 문서를 닫을 때 이벤트 구독을 해제합니다.
객체 변경 이벤트에서는 재계산을 예약하고 `RhinoApp.Idle`에서 명령과 Undo/Redo 종료를 확인한 뒤 계산합니다.

현재 메뉴에서 지원하는 Modifier는 Boolean Difference, Union, Intersection, Mirror, Array, Bend입니다. Array는 직선·격자 배치입니다. Bend의 여러 박스는 독립된 공간 변형을 순서대로 적용하며 임의의 복잡한 자기교차까지 모두 검출하지는 않습니다.
저장 상태 형식은 버전 5이며 기존 버전 1·2·3·4 파일은 읽으면서 변환합니다. 버전 1·2의 Modifier는 Enabled 상태로, 버전 3·4는 저장된 ON/OFF와 기존 설정을 유지하여 복원합니다. 노드 10,000개, 깊이 128단계, JSON 8 MiB까지 허용합니다. 알 수 없는 형식, 순환·중복·끊어진 트리 관계, BasePlane·Control Box 소유권과 종류에 맞지 않는 속성은 복원 전에 검사합니다. Modifier 이름은 공백을 정리한 120자 이내의 한 줄입니다.
추가 Modifier 종류와 복잡한 형상의 추가 성능 개선은 후속 구현 범위입니다.
