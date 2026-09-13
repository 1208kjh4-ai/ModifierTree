# 패널 행 선택과 Rhino 선택 — 0.15.3

Rhino 작업을 저장하고 종료한 뒤 설치한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-plugin.ps1
```

다시 실행하여 `MTreeStatus`에서 **0.15.3.0**을 확인한다. 패널에서 한 행을 선택하면 기존 Select in Rhino 경로를 호출한다. 같은 행의 지연 선택 이벤트가 다시 와도 이미 선택된 Rhino 객체와 검볼은 재생성하지 않는다. 기존 버튼도 유지한다.

## 화면 확인

1. Gumball을 끈 상태에서 결과가 있는 Modifier 행을 선택한다. Rhino에서 결과가 선택되고 검볼이 한 번 켜진 뒤 안정적으로 유지되어야 한다. 명령 기록에 `Gumball`이 계속 반복되지 않아야 한다. 검볼로 움직이면 해당 Modifier의 입력과 제어 객체가 함께 움직여야 한다.
2. 그 안의 원본 행을 선택한다. 해당 편집 단계로 들어가 그 객체만 선택되어야 한다. 이전 전체 결과의 검볼이 남지 않아야 한다.
3. Bend의 Control Box 행과 Mirror의 BasePlane 행을 각각 선택한다. 해당 제어 객체를 검볼로 조작할 수 있어야 한다. Control Box는 휘어진 선으로 표시되어야 한다.
4. Shift 범위 선택과 Ctrl 추가 선택 후 여러 행을 다른 Modifier 안으로 드래그한다. 선택 행이 한 행으로 줄거나 순서가 바뀌지 않아야 한다. 여러 행은 트리 편집용 선택이며, Rhino에는 직전에 선택한 객체가 남을 수 있다.
5. 이름을 더블클릭해 편집하고 Enter 또는 Esc로 마친다. InputWire와 State의 더블클릭, 속성 수치 Enter도 확인한다.
6. 뷰포트에서 다른 항목을 선택하거나 빈 곳을 클릭한다. 패널이 이를 반영하고 이전 행을 다시 선택하지 않아야 한다. Undo 후에도 선택이 반복되거나 화면 갱신이 계속 발생하지 않아야 한다.
7. Show result OFF에서 원본 행을 선택한다. Rhino 기본 객체가 선택되어야 한다. ON으로 돌아온 뒤 다시 다른 행을 선택해 결과·제어 객체 선택을 확인한다.
8. 같은 행을 여러 번 클릭하고 3~5초 기다린 뒤 이동 화살표와 회전 호를 각각 드래그한다. 검볼이 깜빡이거나 드래그 시작점으로 되돌아가지 않아야 한다. 첫 변형 직후 같은 검볼로 두 번째 변형도 수행한다.
9. Modifier 객체를 선택한 채 `Move`와 `Rotate`를 키보드로 실행하고 Esc로 취소한다. 두 명령 모두 즉시 프롬프트를 시작해야 하며 대기 중인 `Gumball` 명령에 가로막히지 않아야 한다.

이름 편집·명령 실행·드래그·구조 복원 중에는 자동 선택을 실행하지 않는다. 빈 Modifier나 선택할 수 없는 입력은 기존 Select in Rhino의 제한을 따른다. 프로그램이 행을 복원하는 화면 갱신은 자동 Rhino 선택을 요청하지 않는다.

## 검사 범위

0.15.3 Release 빌드 경고·오류 0개. RhinoCore를 시작하지 않는 Eto/WPF 검사와 새 선택·검볼 정책 검사 13개가 모두 통과했다. 기존 0.15.2의 **Core 205 + Rhino·Eto/WPF 772 = 977 PASS**, headless Undo/Redo 3 SKIP 기록은 유지한다. 현재 환경에서는 RhinoCore 검증 호스트가 `COM E_FAIL`로 시작되지 않아 0.15.3 전체 native 회귀 검사를 새로 집계하지 않았다. 실제 패널 클릭부터 검볼 표시·드래그까지의 이벤트 순서는 위 수동 확인 대상이다.

원인과 코드 수정 범위는 [0.15.3 검볼 수정 기록](Gumball_Selection_Fix.md)에 정리했다. 기존 기준선은 [0.15.2 빌드 로그](../artifacts/validation/0.15.2/release-build-2026-09-11.txt), [전체 검사 로그](../artifacts/validation/0.15.2/rhino-checks-2026-09-11.txt), [UI 검사 로그](../artifacts/validation/0.15.2/ui-checks-2026-09-11.txt)에서 확인한다.
