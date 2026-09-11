# 0.10.0 Modifier 이름 · Bake · Merge 확인

## 업데이트

Rhino 작업을 저장하고 완전히 종료한 뒤 실행합니다.

```powershell
cd D:\Project_Modifier
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-plugin.ps1
```

Rhino를 다시 시작하고 `MTreeStatus`에서 `0.10.0.0`을 확인합니다. `.rhp`를 새 경로에서 드래그할 필요는 없습니다. 0.9.0에서 저장한 트리를 열어 그대로 사용할 수 있습니다.

## 이름 편집

1. Modifier의 Name 셀을 더블클릭하고 `Main`을 입력합니다. Enter로 완료하면 `(BD) Main`이 보여야 합니다.
2. 다시 더블클릭하면 `Main`만 편집합니다. 이름을 변경하다 Esc를 누르면 이전 이름을 유지해야 합니다.
3. Undo/Redo로 이름이 복원되는지 확인합니다. 이름을 바꿔도 선택과 최종 형상이 유지되어야 합니다.
4. 이름을 빈 값으로 완료하면 `Boolean Difference`로 돌아갑니다. 한글 이름도 입력해 확인합니다.
5. 파일을 저장하고 다시 열어 이름과 기존 트리가 유지되는지 확인합니다.

## Bake와 Merge

기존 트리에 유효한 중첩 Modifier를 준비합니다. 예를 들어 큰 Box A에서 B를 뺀 `(BD) Main`을 상위 Difference의 첫 입력으로 넣고, C를 상위의 두 번째 입력으로 넣습니다.

```text
Boolean Difference
  (BD) Main
    A
    B
  C
```

1. Main을 선택하고 Bake를 누릅니다. 루트 맨 위에 `Main` 행이 추가되고 원래 트리는 유지되어야 합니다. 결과가 원래 프리뷰와 같은 위치이므로 겹쳐 보일 수 있습니다.
2. 새 Main 결과를 조금 이동합니다. 고정 객체 하나로 선택·이동되어야 합니다. 원래 A/B를 움직이면 기존 Modifier만 갱신되고 Bake 결과는 유지되어야 합니다.
3. 이동을 Undo한 뒤 Bake를 Undo합니다. 새 결과 객체와 루트 등록이 함께 없어져야 합니다. Redo하면 같은 결과와 등록이 복원되어야 합니다.
4. 원래 `(BD) Main`을 선택하고 Merge를 누릅니다. 상위의 첫 입력이 `Main` 행으로 바뀌고 A/B와 Main Modifier는 트리에서 없어져야 합니다. C의 위치와 상위 결과는 유지되어야 합니다.
5. Merge 후 기존 A/B는 Rhino에서 숨겨 보관됩니다. Undo 한 번으로 원래 하위 트리와 원본 표시가 복원되고 고정 결과가 제거되어야 합니다. Redo하면 다시 교체되어야 합니다.
6. Merge한 Main을 개별 이동해 상위 Difference가 재계산되는지 확인합니다.
7. Merge 후 저장·다시 열기를 하여 새 Main 객체, 부모·형제 관계와 숨겨 둔 원본이 유지되는지 확인합니다. 파일에는 이전 세션의 Undo 기록은 저장되지 않습니다.

Bake/Merge/원본 이동/트리 드래그를 섞어 연속 Undo/Redo도 확인합니다. 이번 단계는 형상 생성과 트리 변경을 한 기록에 넣으므로, 0.9.0의 트리 편집 단독 확인에 추가로 필요합니다.

## 여러 결과 조각과 빈 결과

- A의 가운데를 B로 완전히 잘라 두 덩어리가 남게 합니다. Bake/Merge 후에도 두 조각을 가진 하나의 Rhino 객체로 선택·이동되어야 합니다. 이 객체를 상위 Modifier의 A와 Cutter로 각각 사용해 결과를 확인합니다.
- A 내부에 작은 B를 완전히 넣어 내부 공간이 생기는 경우, Bake/Merge 후 그 공간이 채워지지 않아야 합니다.
- B가 A를 완전히 포함해 결과가 Empty라면 Bake/Merge 버튼이 비활성화되고 객체를 만들지 않아야 합니다.
- 입력이 누락되거나 오류로 이전 프리뷰만 남아 있으면 오래된 결과를 생성하지 않아야 합니다.
- 잠긴 원본이 있으면 Merge가 실패 이유를 명령 기록에 표시하고 기존 트리·원본을 유지해야 합니다.

## 구현 메모

Bake/Merge는 실행 시 문서의 현재 원본으로 새로 계산합니다. 미리보기 캐시나 이동 도중 임시 형상은 확정하지 않습니다. 결과의 이름과 기본 레이어·재질·색상은 Modifier 이름과 첫 원본 속성을 사용합니다. 원본 그룹은 결과에 복사하지 않습니다. 첫 원본 레이어가 숨김·잠금이면 현재 레이어를 사용합니다.

형상과 트리 변경을 먼저 준비·검사한 뒤, 네이티브 객체 생성 및 원본 숨김과 트리 Undo를 동일 Rhino 기록에 넣습니다. 실패 시 생성 결과를 제거하고 바뀐 원본 표시와 트리를 되돌립니다. Rhino 검사 호스트에서 연속 Undo/Redo는 제한이 있으므로 실제 Rhino에서 위 절차를 확인해야 합니다.

## 검증 결과와 남은 확인

2026-09-09, 0.10.0 Release 빌드 오류·경고 0개. Core 84개와 실제 Rhino 호스트 검사 196개, 총 280개가 통과했습니다.

- 기존 schema 1 파일 변환, 이름 저장·복원과 이름 Undo의 캐시 유지.
- Bake 시 원래 트리 보존, Merge 시 정확한 부모·형제 위치 유지, 상위 결과 부피 유지.
- 결과 객체와 트리·원본 표시의 단일 Undo, 기존 Rhino 명령 기록에 합류.
- 오류 발생 후 객체·트리·원본 숨김 복구, 잠김·누락·Empty·읽기 전용 상태에서 변경 없이 거부.
- 여러 결과 조각을 한 Rhino 객체로 생성하고 상위의 첫 입력·Cutter로 재사용. 기존 공동과 떨어진 다른 솔리드가 함께 있는 입력도 확인.
- Bake/Merge 버튼을 포함한 실제 Eto/WPF 레이아웃의 180/300/500/760 너비 적응.

자동 검사는 이름 더블클릭의 실제 마우스 이벤트와 플러그인 파일 훅 전체를 대신하지 않습니다. 새 결과를 만든 뒤의 저장·열기와 연속 Undo/Redo는 위 실제 Rhino 확인 절차가 남아 있습니다.

**별도 Boolean 확인 항목:** 화면 없는 Rhino 검사 환경에서 큰 Box 내부에 Cutter Box가 완전히 들어갈 때, 기존 `CreateBooleanDifferenceWithIndexMap`은 기대한 새 공동을 만들지 않고 바깥 형상을 반환했습니다. 일반 Boolean 오버로드도 해당 검사에서 예상과 다른 결과를 반환했습니다. 이번 변경의 공동 검사는 이미 존재하는 공동을 보존하는지를 검증한 것이며, 완전히 포함된 Cutter로 새 공동을 생성하는 기존 경로를 수정한 것은 아닙니다. 위 내부 Cutter 사례는 실제 Rhino에서도 별도로 확인해야 합니다.

한 Brep에 떨어진 여러 솔리드가 들어 있으면 Rhino Boolean이 일부 솔리드를 누락할 수 있어, 계산 시 `GetConnectedComponents`로 나누고 inward 공동 껍질은 자신을 포함하는 바깥 솔리드에 유지합니다. 소속이 불명확하면 계산을 거부합니다. [Brep.Append](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.brep/append)는 조각을 새 Brep 하나에 복사하며, [Custom Undo](https://developer.rhino3d.com/samples/rhinocommon/custom-undo/)는 내부 상태의 반대 스냅샷을 등록합니다.
