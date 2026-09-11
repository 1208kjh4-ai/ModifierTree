# Mirror · Array 조작 확인 — 0.13.0

Rhino 문서를 저장하고 Rhino를 종료한 다음 `scripts/install-plugin.ps1`로 업데이트한다. 다시 실행한 Rhino에서 `MTree`로 패널을 열어 아래 항목을 확인한다. 다른 경로의 RHP를 추가로 끌어 넣을 필요는 없다.

## Mirror 기준 평면

1. 닫힌 Box를 등록하고 Mirror를 추가한다. 새 Mirror 아래에 `BasePlane`이 생기고, 속성에서 `Keep original`과 `Boolean Union`이 켜져 있어야 한다.
2. Box를 Mirror 안으로 넣고 `Set Plane`을 누른다. 원점 → 평면의 X축 위 점 → 평면 위의 세 번째 점 순서로 찍는다. 마지막 점을 정하는 동안 사각형과 법선 방향이 보여야 한다. 사각형은 편집용 표시이며, 반사는 무한 평면을 기준으로 계산된다.
3. 각 단계에서 Esc로 취소하거나 같은 점·일직선의 세 점을 지정한다. 기존 평면과 결과가 바뀌지 않아야 한다.
4. 뷰포트의 Mirror 결과를 더블클릭하여 내부 편집에 들어가 `BasePlane`을 선택한다. Move와 검볼 이동·회전으로 평면을 바꾸면 최종 결과도 갱신되어야 한다. BasePlane이 결과의 닫힌 솔리드 입력으로 계산되면 안 된다.
5. 내부 편집에서 나와 Mirror 전체를 선택하고 이동·회전한다. 원본, BasePlane, 반사 결과가 함께 따라가야 한다. BasePlane 행을 같은 Mirror 안에서 재정렬해도 역할은 유지되고 다른 Modifier로 옮길 수 없어야 한다.
6. 서로 겹치는 원본과 반사본으로 `Boolean Union` ON/OFF를 비교한다. ON은 합집합, OFF는 별도 복사본이다. 떨어져 있는 솔리드는 ON이어도 각각 남는다. `Keep original` OFF는 반사본만 남긴다.
7. 평면을 정한 작업과 원본·평면 변형을 각각 Undo/Redo한다. 평면과 결과가 함께 돌아와야 한다. 실패한 Union은 실패 상태를 표시해야 한다.

## Bake · Merge에서 BasePlane 유지

1. Mirror를 Bake한다. 원래 트리와 BasePlane 위치·방향·크기가 유지되고, 고정 결과만 루트에 추가되어야 한다.
2. Bake된 결과를 이동한다. 원래 BasePlane과 Mirror 결과는 따라 움직이면 안 된다.
3. Mirror를 Merge한다. 해당 트리 위치가 고정 결과로 바뀌고 BasePlane은 기존 위치에서 숨겨져야 한다. 결과에 평면이 섞이면 안 된다.
4. Merge 결과를 이동해도 숨겨진 BasePlane은 움직이지 않아야 한다. 이동을 되돌린 뒤 Merge를 Undo/Redo하여 원래 트리와 평면 표시가 복원되는지 확인한다.
5. Mirror 하위 트리를 Duplicate한다. 복제된 BasePlane을 움직였을 때 원래 Mirror의 평면과 결과는 유지되어야 한다.

## Array 방향 설정

1. Array에 Box를 넣는다. 기본 축이 world X/Y/Z인지 확인하고 Count X=3, Y=2, Z=1로 설정한다.
2. X의 `Set X Axis`에서 두 점을 찍어 대각 방향을 지정한다. 첫 점에서 두 번째 점으로 향하는 화살표가 보여야 한다. 점 사이 거리와 위치가 달라도 방향만 바뀌고 Spacing과 원본 위치는 유지되어야 한다.
3. Y에도 X와 직교하지 않는 방향을 지정한다. 두 축이 독립적으로 유지되고 사선 격자가 만들어져야 한다. 음수 Spacing은 해당 축의 반대 방향으로 배열되어야 한다.
4. `Reset X Axis`를 누른다. X만 world X로 돌아오고 Y/Z, Count, Spacing은 유지되어야 한다.
5. 점 선택 중 Esc 또는 같은 점 두 번을 입력한다. 기존 축이 유지되어야 한다. Set/Reset을 Undo/Redo하여 축과 결과가 복원되는지 확인한다.
6. Count·Spacing을 입력하고 아직 Enter로 확정하지 않은 상태에서 축을 설정한다. 입력 중이던 값은 남아 있어야 하며, Enter로 적용할 때 새 축을 초기화하면 안 된다. 0.14.2부터 Array에는 Apply 버튼이 없다.

## 전체 회전과 내부 편집 구분

1. Array 전체를 한 번 클릭하여 선택하고 Rhino Rotate 또는 검볼로 90도 회전한다. 원본과 축이 함께 회전하여 배열 전체가 같은 형태로 돌아가야 한다. 조작 중 프리뷰도 같은 방향이어야 한다.
2. 뷰포트의 Array 결과를 더블클릭하여 내부 원본만 회전한다. 축 방향은 유지되고 복사본 각각의 자세만 바뀌어야 한다.
3. 상위 Modifier 안에 Array와 Mirror를 넣고 상위를 회전한다. 중첩 Array의 축과 Mirror의 BasePlane도 따라가야 한다.
4. 전체 이동은 축 방향을 바꾸면 안 된다. 변형 중 Esc로 취소하면 원본과 축 모두 이전 상태로 돌아와야 한다.
5. 각 변형 직후 Undo 한 번으로 원본 위치와 Array 축이 함께 복원되는지, 이어서 Redo가 전체 변형을 복원하는지 확인한다. 여러 번 연속 Undo/Redo도 실제 Rhino에서 확인한다.

## 저장과 좁은 패널

- 사용자 지정 평면·축, Union, OFF 상태, 이름을 포함한 문서를 저장하고 닫았다가 다시 연다. 트리 역할과 결과가 같아야 한다.
- 0.12 문서의 Mirror는 이전 결과를 보존하도록 Union OFF와 기존 수치 평면으로 복원된다. 이 Mirror에서 Set Plane을 실행하면 실제 BasePlane 제어 객체가 추가되어야 한다. 새로 만드는 Mirror는 Union ON이다.
- 패널 폭을 약 180px까지 줄인다. Set Plane, 축별 Set/Reset, 나머지 작업 버튼이 패널 폭을 따르고 아래쪽은 세로로 스크롤되어야 한다.
- 트리에서 BasePlane은 `Control`로 표시되고 이름 수정·InputWire·개별 Remove 대상이 아니어야 한다. 패널의 Select in Rhino와 Move로는 조작할 수 있어야 한다.

키보드·마우스 점 선택, 검볼 드래그, 실제 Rhino의 연속 Undo/Redo는 위 수동 확인이 필요하다.
