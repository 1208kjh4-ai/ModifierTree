# Modifier Manager UI 수정 위치 — 0.15.3

UI는 `src/ModifierTree.Rhino/UI/`의 C# 파일에 있으며, Eto.Forms 컨트롤을 코드로 배치한다. 한 파일의 한 영역에 전부 모여 있는 구조는 아니다. 배치만 바꾸려면 `ManagerLayout.cs`부터 보면 된다.

| 수정할 내용 | 파일 | 시작 줄 |
| --- | --- | ---: |
| 전체 배치, 하단 버튼 순서, 여백, 스크롤 영역 | [ManagerLayout.cs](../src/ModifierTree.Rhino/UI/ManagerLayout.cs) | 8 |
| Bake·Merge·Remove의 균등 너비와 간격 | [ManagerLayout.cs](../src/ModifierTree.Rhino/UI/ManagerLayout.cs) | 42 |
| 버튼 이름과 툴팁 | [ModifierManagerPanel.cs](../src/ModifierTree.Rhino/UI/ModifierManagerPanel.cs) | 18 |
| 버튼 클릭 동작과 메뉴 생성 | [ModifierManagerPanel.cs](../src/ModifierTree.Rhino/UI/ModifierManagerPanel.cs) | 44 |
| 행 선택 시 Rhino 자동 선택 연결 | [ModifierManagerPanel.cs](../src/ModifierTree.Rhino/UI/ModifierManagerPanel.cs) | 119 |
| 선택에 따른 버튼 활성화 조건 | [ModifierManagerPanel.cs](../src/ModifierTree.Rhino/UI/ModifierManagerPanel.cs) | 384 |
| Name·Enabled·Mirror·Array·Bend 속성 영역의 배치 | [ModifierPropertiesEditor.cs](../src/ModifierTree.Rhino/UI/ModifierPropertiesEditor.cs) | 46 |
| Enter 입력 처리 | [ModifierPropertiesEditor.cs](../src/ModifierTree.Rhino/UI/ModifierPropertiesEditor.cs) | 89 |
| 선택한 Modifier·Control Box의 값과 컨트롤 표시 여부 | [ModifierPropertiesEditor.cs](../src/ModifierTree.Rhino/UI/ModifierPropertiesEditor.cs) | 97 |
| 속성 입력 검증과 적용 요청 | [ModifierPropertiesEditor.cs](../src/ModifierTree.Rhino/UI/ModifierPropertiesEditor.cs) | 164 |
| Control Box 이름·Strength·Mode 입력 검증 | [ModifierPropertiesEditor.cs](../src/ModifierTree.Rhino/UI/ModifierPropertiesEditor.cs) | 203 |
| Count·Spacing의 X/Y/Z 입력칸 생성 | [ModifierPropertiesEditor.cs](../src/ModifierTree.Rhino/UI/ModifierPropertiesEditor.cs) | 248 |

줄 번호는 0.15.3 기준이며 파일을 편집하면 달라질 수 있다. `Create`, `CreateResultActions`, `UpdateSelection`, `SubmitOnEnter`, `VectorFields`로 검색하면 해당 코드를 찾을 수 있다.

## 자주 조절할 부분

`ManagerLayout.Create`의 `Items` 순서는 컨트롤의 표시 순서다. `Spacing`은 컨트롤 사이 간격이고 `Padding`은 바깥 여백이다. Bake·Merge·Remove의 순서는 `CreateResultActions(bake, merge, remove)` 호출에서 바꾸고, 균등 너비 계산과 간격은 같은 파일의 `CreateResultActions`에서 정한다. 실제 행 너비를 3등분하므로 정수 픽셀 반올림에 따른 차이는 최대 1px이다. 트리 열 너비는 `FitColumns`에서 정한다.

버튼의 `Text`를 바꾸면 표시 문구가 바뀌고, `ToolTip`을 바꾸면 마우스를 올렸을 때의 설명이 바뀐다. 클릭 동작은 생성자의 `.Click += ...` 연결을 찾으면 된다. 작업 중인 문서·선택·Undo 상태를 검사하는 조건은 배치와 별개다.

`SelectionChanged`는 실제 행 key 집합이 바뀐 경우에만 `UpdateSelection(selectInViewport: true)`를 호출하여 한 행 선택을 기존 `SelectSource` 동작에 연결한다. `RefreshTree` 끝의 `UpdateSelection()`은 기본값 false를 사용하므로 상태 갱신이 다시 Rhino 선택을 발생시키지 않는다. `DocumentSession.SelectInViewport`도 같은 노드와 정확한 persistent 선택을 확인해 중복 `UnselectAll`·재선택·검볼 요청을 막는다. 다중 행 선택은 트리 편집용으로 유지한다. 자동 선택 후 세션의 선택 revision을 기록하는 순서를 유지해야 다음 갱신에서 Shift/Ctrl 선택이 잘못 초기화되지 않는다.

Count·Spacing에서는 Enter를 눌러 현재 Array 입력값을 함께 적용한다. Control Box의 Strength도 Enter로 적용하고 Limited/Unlimited 선택은 즉시 적용한다. Name에서도 Enter로 적용할 수 있다. 숫자를 입력하는 도중이나 Tab으로 이동할 때는 적용하지 않는다. Array와 Control Box에는 Apply 버튼이 없으며, 다른 Modifier의 Apply 버튼은 유지한다. 잘못된 숫자는 적용하지 않고 입력 영역 아래에 오류를 표시한다.

## 수정 후 빌드·반영

프로젝트 루트에서 빌드한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release
```

레이아웃과 키 입력만 확인하려면 RhinoCore를 시작하지 않는 검사를 실행한다. 설치된 Eto/WPF 라이브러리는 사용하지만 Rhino 라이선스는 필요하지 않다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-rhino.ps1 -Configuration Release -UIOnly
```

Rhino 작업을 저장하고 종료한 뒤 설치한다. 다시 시작하여 `MTreeStatus`에서 버전을 확인한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-plugin.ps1
```

실제 Rhino에서는 Count와 Spacing 각각에서 Enter 적용, Undo, 잘못된 입력 처리, 패널 폭 변경, 세 버튼의 선택별 활성화 상태를 확인한다.
