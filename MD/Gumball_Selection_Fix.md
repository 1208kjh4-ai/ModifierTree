ㅋ# Modifier 선택 시 검볼 반복·명령 차단 수정 — 0.15.3

## 증상

Modifier Manager에서 관리 중인 객체를 선택하면 검볼이 매우 짧게 켜졌다 꺼지는 것처럼 보이고, 이동·회전 핸들 드래그가 시작되지 않았다. 일반 Rhino 객체에서는 문제가 없었으며, 때때로 아래 메시지가 반복됐다.

```text
Modifier Tree: could not activate Gumball. Run Gumball On to enable it.
```

Modifier 객체가 선택된 동안에는 다른 Rhino 명령도 바로 시작되지 않는 경우가 있었다.

## 원인

패널의 WPF 트리가 갱신 뒤 같은 행에 대한 지연 `SelectionChanged` 이벤트를 다시 보낼 수 있다. 기존 `SelectInViewport`는 같은 노드와 같은 Rhino 객체가 이미 선택되어 있어도 매번 전체 선택을 해제하고 다시 선택했다. 이 과정에서 현재 검볼이 철거됐다.

검볼 활성화는 명령 밖의 `RhinoApp.RunScript("_Gumball _On")` 호출이라 Rhino 명령 큐에서 비동기로 처리된다. 반복 선택 이벤트마다 같은 명령을 추가하면 검볼이 계속 다시 만들어지고, 대기 중인 Gumball 명령이 사용자 이동·회전 명령과 충돌할 수 있었다. 또한 Rhino의 검볼 hit-test가 마우스 누름 순간 일시적으로 `None`을 반환하면 플러그인의 뷰포트 picker가 클릭을 취소하고 같은 객체를 다시 선택할 수 있었다.

## 수정

- 패널은 이전과 현재의 행 key 집합이 같으면 자동 선택을 다시 실행하지 않는다.
- 세션은 노드 ID뿐 아니라 실제 선택된 GUID 집합과 persistent 선택 상태가 정확히 일치하는지 확인한다. 일치하면 `UnselectAll`, 객체 재선택, revision 증가와 화면 갱신을 생략한다.
- 검볼 요청과 실행을 뷰포트 선택 revision에 묶고, 같은 revision에서는 `_Gumball _On`을 최대 한 번만 요청한다. 선택이 바뀐 뒤 남은 stale 요청도 버린다.
- 현재 네이티브 선택과 표시 형상이 일치하고 검볼이 보이면 같은 객체 위의 마우스 누름을 Rhino에 넘겨 검볼 드래그가 시작되도록 한다.
- 굽힌 Bend Control Box는 표시 cage와 네이티브 box가 다를 수 있으므로 이 마지막 pass-through 최적화에서 제외하고 기존 관리 picker를 유지한다.

## 자동 확인

- Release 빌드: 경고 0개, 오류 0개.
- RhinoCore를 띄우지 않는 Eto/WPF 검사: 전부 통과.
- 추가 정책 검사 13개: 동일 선택 멱등성, persistent 선택 복구, 패널 동일 행 억제, visible Gumball 입력 보존, Control Box 예외, revision별 1회 요청, stale 요청 폐기를 확인했다.
- 전체 RhinoCore 검증은 현재 PC에서 호스트 시작 자체가 `COM E_FAIL`과 `dotnet.exe` 네이티브 오류로 실패해 재실행하지 않았다. 이는 플러그인 검사에 들어가기 전의 호스트 시작 실패이며, 실제 마우스 동작은 아래 절차로 확인한다.

## Rhino 화면 확인

1. Rhino를 시작하고 `MTreeStatus`가 `0.15.3.0`인지 확인한다.
2. 결과가 있는 Modifier 행을 한 번 선택한 뒤 3~5초 기다린다. 검볼이 안정적으로 남고 명령 기록에 `Gumball`이 반복되지 않아야 한다.
3. 검볼의 X/Y/Z 이동 화살표와 회전 호를 각각 드래그한다. 드래그가 즉시 시작되고 결과와 하위 입력이 함께 변해야 한다.
4. 첫 변형을 끝낸 뒤 선택을 바꾸지 않고 같은 검볼로 두 번째 변형을 수행한다.
5. Modifier 객체를 선택한 상태에서 `Move`, `Rotate`를 실행한다. 명령 프롬프트가 즉시 시작되어야 한다.
6. Modifier 결과, 그 안의 원본, Mirror BasePlane을 차례로 선택해 같은 동작을 확인한다.
7. Bend Control Box는 휘어진 cage 선을 다시 클릭한 경우와 이미 선택된 상태의 검볼 이동·회전·스케일을 각각 확인한다.
8. 겹친 객체가 있는 위치에서도 검볼 핸들 드래그가 아래 객체 선택으로 바뀌지 않는지 확인한다.

검볼 활성화가 한 번 실패했다는 메시지만 나오고 반복되지는 않는다면 `Gumball On`을 직접 한 번 실행하거나 다른 행을 선택했다가 돌아온다. 문제가 재현되면 `MTreeTrace`를 켠 뒤 한 번 조작하고, `MTreeDumpLog` 출력과 화면을 함께 기록한다.
