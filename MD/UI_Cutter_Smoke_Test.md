# Modifier Tree 0.5.0 UI·Cutter 화면 확인

Rhino 작업을 저장하고 재시작한 뒤 `artifacts/bin/0.5.0/Release/net8.0-windows/ModifierTree.rhp`를 로드합니다. 같은 폴더의 동반 파일도 유지합니다.
`MTreeStatus`에서 `0.5.0.0`을 확인하고 겹치는 A와 B를 Difference 자식으로 넣습니다.

## Cutter 선택과 이동

- [ ] InputWire OFF, 선택 해제 상태에서 Cutter가 보이지 않는다.
- [ ] Rhino에서 Cutter를 선택하거나 패널에서 해당 행을 선택하면 모서리가 노란색으로 보인다.
- [ ] Rhino와 패널에서 선택을 해제하면 InputWire OFF인 Cutter는 다시 숨겨진다.
- [ ] Select in Rhino → Rhino Move 중 최종 결과는 유지되고, 움직이는 Cutter는 면 없이 모서리만 보인다.
- [ ] Gumball 이동도 확인한다. 모서리가 시작 위치에 남지 않고 현재 위치를 따라야 한다.
- [ ] 패널 B 선택 → Move에서도 최종 결과와 이동 모서리가 유지된다.
- [ ] 이동 확정 후 결과가 새 위치로 재계산된다. 이동 중에는 마지막 확정 결과를 유지한다.
- [ ] Esc 취소 후 입력과 최종 결과가 원래 상태이며 임시 이동 선이 남지 않는다.
- [ ] Undo/Redo 후 원본 위치와 최종 결과가 맞는다.

현재 정상 계산에 사용된 원본을 대상으로 표시를 제어합니다. 계산 실패 시 원본을 다시 보여주는 복구 동작은 유지합니다. Show result OFF에서는 Rhino 원본의 일반 표시를 사용합니다.

## 더블클릭

- [ ] Name 셀 더블클릭 → 이름을 같은 셀에서 편집한다. 더블클릭만으로 Rhino 선택 명령이 실행되지 않는다.
- [ ] 한글 이름 입력 → Enter 후 Rhino Properties의 이름도 변경된다.
- [ ] 다른 이름 입력 중 Esc → 기존 이름이 유지되며 다음 더블클릭 편집도 정상이다.
- [ ] 이름 변경 Undo/Redo, 이름 없는 원본의 편집, 잠긴 원본의 변경 거부를 확인한다.
- [ ] InputWire 셀 더블클릭 → ON/OFF가 한 번씩 전환되며 다른 행의 설정은 바뀌지 않는다.
- [ ] OFF로 바꿔도 현재 선택 중인 입력은 임시 모서리가 유지된다. 다른 행으로 옮기고 Rhino 선택도 해제하면 숨겨진다.
- [ ] 단일 클릭은 ON/OFF를 바꾸지 않는다. 행 드래그 정렬도 여전히 동작한다.

## 패널 배치

- [ ] Add Object 바로 아래 Add Modifier가 있다.
- [ ] Select in Rhino, Move, Remove가 각각 한 줄씩 있다.
- [ ] 객체/Modifier 개수와 하단의 설명·세션 안내 문구가 없다.
- [ ] 패널을 넓혔다 좁혀도 버튼이 너비를 채우고 오른쪽 버튼이 사라지지 않는다.
- [ ] 트리의 열 너비도 함께 줄어든다. 긴 이름은 제한된 셀 안에 표시되며 전체 패널을 늘리지 않는다.
- [ ] 기존의 접기/펼치기, 드래그 입력 재배치, root 영역 드롭이 유지된다.

자동 검사는 Core/GUID 30개와 Rhino·문서·WPF 레이아웃 77개를 통과했습니다. 이름 변경은 원본 GUID·속성·형상 보존과 Undo를 검사했습니다. 레이아웃은 실제 Eto/WPF 컨트롤을 창 없이 여러 폭으로 배치해 검사했습니다.
마우스 입력, Gumball/Move 동적 표시와 UI Redo는 위 수동 확인 대상입니다.

동적 표시에는 Rhino 공식 [GetDynamicTransform](https://developer.rhino3d.com/api/rhinocommon/rhino.docobjects.rhinoobject/getdynamictransform)을 사용하며 문서 원본의 숨김·재질 속성은 변경하지 않습니다.
