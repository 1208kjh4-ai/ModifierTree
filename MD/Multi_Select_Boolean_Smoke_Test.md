# 0.11.0 다중 선택 드래그와 Boolean Union / Intersection

2026-09-11 사용자로부터 0.11.0의 기본 동작이 정상이라는 확인을 받았습니다. 아래 개별 항목 전부를 별도로 확인한 기록은 아닙니다.

## 업데이트

Rhino 작업을 저장하고 완전히 종료한 뒤 실행합니다.

```powershell
cd D:\Project_Modifier
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-plugin.ps1
```

재시작 후 `MTreeStatus`에서 `0.11.0.0`을 확인합니다. 기존 `.rhp`를 다시 드래그하지 않습니다. 0.9.0과 0.10.0에서 저장한 파일도 열 수 있습니다.

## Shift 범위 선택 후 한 번에 넣기

1. 원본 객체 A, B, C, D를 등록하고 빈 Modifier를 만듭니다.
2. A 행을 클릭하고 **Shift를 누른 채 C 행을 클릭**합니다. A/B/C 세 행이 선택되어야 합니다.
3. 선택된 A/B/C 중 한 행을 잡아 Modifier 행 위로 드래그합니다. 세 행이 **A → B → C** 순서로 함께 들어가야 합니다. 선택한 묶음은 이동 후에도 선택되어야 합니다.
4. Undo 한 번으로 세 행 모두 원래 부모와 위치로 돌아와야 합니다. Redo 한 번으로 다시 들어가야 합니다.
5. 이번에는 C를 먼저 선택한 뒤 Ctrl+클릭으로 A를 추가합니다. 두 행을 드래그하면 클릭 순서가 아닌 트리 순서인 **A → C**로 들어가야 합니다.

대상 Modifier 자체를 선택 묶음에 포함하면 그 안으로 드롭할 수 없습니다. Modifier가 자기 자신의 자식이 되는 구조를 방지하기 위한 동작입니다.

## 순서·부모 변경과 선택 유지

- 한 Modifier 안에서 B/C를 선택해 A 위·아래 행 사이로 옮깁니다. B/C의 상대 순서가 유지되어야 합니다.
- 다른 부모 아래에 있는 원본을 Ctrl로 함께 선택해 새 Modifier로 옮깁니다. 원래 트리의 위아래 순서로 들어가야 합니다.
- 여러 행을 하단 Drop here to move to root에 드롭하면 루트로 함께 이동해야 합니다.
- 부모 Modifier와 그 자식을 함께 선택해 옮기면 부모 안의 기존 자식 관계를 유지해야 합니다. 자식이 별도로 꺼내지면 안 됩니다.
- 선택한 부모를 자신의 자손 안에 넣으려 하면 전체 드롭이 거부되어야 합니다. 일부만 이동하면 안 됩니다.
- 같은 위치에 드롭하면 새 Undo 단계나 문서 수정 표시가 생기지 않아야 합니다.
- 다중 선택 후 Show result를 켜고 꺼서 패널을 갱신해도 선택이 유지되어야 합니다. 이후 뷰포트에서 특정 결과를 클릭하면 그 결과의 단일 선택으로 바뀌어야 합니다.
- 행을 일반 클릭하고 드래그하지 않으면 단일 선택으로 바뀌며, 이름 더블클릭과 InputWire 더블클릭도 기존처럼 작동해야 합니다.

여러 행을 선택한 상태에서는 단일 행용 Select in Rhino, Move, Remove, Bake/Merge 버튼을 비활성화합니다. 이번 다중 선택 기능은 트리 내 배치를 대상으로 합니다.

## Union / Intersection

Add Modifier 메뉴에서 Boolean Difference, Boolean Union, Boolean Intersection 세 종류를 선택할 수 있어야 합니다. 새 Modifier는 비어 있으며 입력을 직접 넣습니다. 모든 Boolean은 닫힌 Brep/Extrusion 입력 두 개 이상을 사용합니다.

1. 겹치는 Box A/B를 Union에 넣으면 합친 결과가, Intersection에 넣으면 겹치는 부분만 보여야 합니다.
2. 세 번째 Box를 추가해 Union은 세 입력 전체를 합치고, Intersection은 세 입력 모두에 공통인 부분만 남기는지 확인합니다.
3. 겹치지 않는 두 객체의 Union은 두 조각을 모두 유지하고, Intersection은 Empty가 되어야 합니다. 한 Box가 다른 Box 안에 완전히 들어가면 Intersection은 작은 Box여야 하며, 같은 크기·위치의 두 Box는 원래 형상을 유지해야 합니다.
4. 이름을 지정하면 `(BU) Body`, `(BI) Common`으로 표시되어야 합니다. 이름 변경·저장·복원·Undo도 확인합니다.
5. `(BD) Main`과 `(BD) Sub` 두 Modifier를 Shift/Ctrl로 선택해 Union에 한 번에 넣습니다. 두 Difference 결과의 합집합이 보여야 합니다.
6. 내부 원본 이동과 전체 Union 이동을 각각 확인합니다. 전체 평행이동에서는 기존 결과 재사용, 개별 이동에서는 상위까지 재계산되어야 합니다.
7. Union과 Intersection 각각 Bake/Merge하여 고정 결과와 트리 배치, 한 번의 Undo 및 Redo를 확인합니다.
8. 혼합 트리를 저장하고 Rhino를 재시작해 종류·이름·순서·표시 설정이 유지되는지 확인합니다.

Union은 빈 자식 결과를 건너뛰고, Intersection은 하나라도 빈 자식이 있으면 Empty입니다. Intersection 입력 하나가 여러 조각으로 이루어져 있어도 조각 전체를 한 입력으로 계산합니다. 두 번째 이후라는 이유만으로 Union/Intersection 입력을 Cutter로 표시하지 않습니다.

## 검사 범위

2026-09-10 Release 0.11.0 빌드: 오류·경고 0개. Core 103개와 Rhino 293개, 총 396개 검사가 통과했습니다. 화면 없는 호스트에서 지원되지 않는 연속 Undo/Redo 관련 3개 항목과 실제 키·마우스 조작은 수동 확인 대상입니다. [빌드 기록](../artifacts/validation/0.11.0/release-build-2026-09-10.txt), [Rhino 검사 기록](../artifacts/validation/0.11.0/rhino-checks-2026-09-10.txt).

Core에서 묶음 순서·부모 변경·순환 방지·원자적 거부·불변 ID와 새 Boolean 종류의 저장 상태를 검사합니다. 실제 Rhino 호스트에서는 묶음 편집의 단일 Undo, 연산 부피와 조각 수, 중첩 결과, 이동 캐시, Bake/Merge 및 Eto/WPF 다중 선택 복원을 검사합니다.

형상 검사는 완전 포함·동일 형상·면 접촉과 내부 공동을 포함합니다. 공동 안의 작은 객체, 공동 경계를 가로지르는 객체, 공동과 정확히 같은 객체, 두 공동의 겹침을 양방향 Intersection으로 확인합니다. 내부 공동들이 합쳐져 다시 섬을 둘러싸는 복잡한 다중 셸 배치는 추가 검증 범위입니다.

Eto/WPF의 Extended 선택 모드를 사용하며, MouseDown 이후 native 선택이 완료된 MouseMove에서 드래그 대상을 확정합니다. 패널 갱신 시 펼쳐진 행의 순서에 맞춰 현재 행 참조로 선택을 복원합니다. 실제 Shift/Ctrl 키와 마우스를 누르는 흐름, 파일 전체 열기 경로 및 연속 Undo/Redo는 위 수동 확인이 필요합니다.

0.10.0에서 관찰한 완전히 포함된 Cutter의 기존 Boolean 검사 환경 문제는 [기존 확인 문서](Bake_Merge_Smoke_Test.md)의 별도 확인 항목으로 유지합니다.
