# Bend 화면 확인 — 0.15.1

자동 검사 결과와 로그는 문서 끝에 기록한다. 창 없는 검사로 실제 마우스·검볼·플러그인 자동 로드 및 파일 훅까지 검증한 것은 아니다. 아래는 사용자가 실제 Rhino에서 확인할 순서다.

## 설치

Rhino 작업을 저장하고 종료한 뒤 실행한다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\Project_Modifier\scripts\install-plugin.ps1
```

다시 실행하여 `MTreeStatus`가 **0.15.1.0**인지 확인한다. 기존 플러그인 ID는 유지되므로 새 버전 RHP를 반복 드래그하지 않고 설치 스크립트로 등록 경로를 갱신한다.

## 첫 Bend

1. World XY 기준 X=10, Y=100, Z=8 정도의 세로로 긴 닫힌 박스를 만든다. 예를 들어 원점 `(-5,0,-4)`에서 반대 꼭짓점 `(5,100,4)`다.
2. **Add Object**로 등록하고 **Add Modifier → Bend**를 만든다.
3. 원본 행을 Bend 안에 넣는다. 처음에는 `Needs Control Box` 상태가 정상이다.
4. Bend 행을 선택하고 하단 속성에서 **Add Control Box**를 누른다. 입력을 감싸는 박스가 생기고 박스만 선택되어야 한다. 기본 Strength는 **45°**, Mode는 **Limited**다.
5. Strength에 `90`을 입력하고 Enter를 누른다. 결과가 즉시 바뀌어야 한다. `0`은 원형, `-90`은 반대 굽힘이다. Apply 버튼은 없다.
6. `NaN`, `181` 등 지원하지 않는 값은 오류를 표시하고 기존 결과·설정을 유지해야 한다.

```text
▼ Bend
  Control Box
  Brep A
```

박스의 로컬 Y축이 길이 방향이고 로컬 +X 쪽으로 굽힌다. 박스 회전으로 방향을 바꾸며 Angle 입력은 없다. Keep Y-Axis Length는 각 박스의 중립축 길이 기준으로 고정 적용된다.

## Control Box 조작

1. Control Box 행에서 **Select in Rhino**를 눌러 휘어진 윤곽선·중간 단면선·중립축과 검볼이 보이는지 확인한다. Strength `0`, `90`, `-90`을 비교하면 박스 표시도 직선, 한쪽 굽힘, 반대 굽힘으로 바뀌어야 한다. 선택된 선은 노란색이다.
2. 박스를 X 방향으로 조금 이동한다. 원본 Brep은 그대로이고 결과 굽힘만 바뀌어야 한다. 이동 중에도 결과와 휘어진 박스가 표시되고, Esc로 취소하면 둘 다 원래 표시로 돌아와야 한다.
3. 박스를 Y축 주위로 회전해 굽히는 방향을 바꾼다. Control Box는 회전된 축을 유지해야 한다.
4. 로컬 Y축으로 박스 높이를 조절한다. 동일 Strength에서 굽힘 반지름과 범위가 달라지고, 조절 중에도 휘어진 표시가 새 높이에 맞아야 한다. X축 폭만 바꾸면 중립축의 굽힘은 유지되어야 한다. 회전된 박스를 월드 축으로 비균등 스케일하면 전단이 될 수 있으므로 검볼 정렬을 박스의 로컬 축에 맞춘다.
5. **Fit to inputs**를 누른다. 박스 방향·Strength·Mode·이름은 유지되고 입력 크기로 맞춰져야 한다. Fit은 적용 전 숫자 초안을 제출하지 않는다.
6. 박스의 Y 높이를 원본보다 짧게 만든 뒤 Limited/Unlimited를 비교한다. Limited 밖은 끝단 방향으로 직선 연결, Unlimited 밖도 휘어져야 한다. X/Z 폭은 변형 대상의 포함·제외 범위가 아니다.
7. 박스 Name을 Enter 또는 트리 더블클릭으로 바꾼다. 이후 뷰포트에서 박스를 선택하고 Undo하여 이름과 표시가 정상 복원되는지 확인한다.
8. 내부 편집에서 선택을 해제하고 하늘색으로 보이는 휘어진 선을 일반 클릭한다. Control Box가 선택되어 검볼로 조작할 수 있어야 한다. 선 사이의 보이지 않는 면만 클릭해서 박스가 잡히면 안 된다.
9. **Show result OFF**로 원래 직교 박스 표시를 확인한다. 이 상태에서도 `MTreeMove` 중 박스의 이동 피드백이 보여야 한다. 다시 ON하면 현재 설정의 굽힌 표시로 돌아온다.

곡선은 표시·일반 클릭용이며 Rhino 문서에 별도 객체로 추가하지 않는다. 검볼의 기준은 원래 직교 박스다. 명령 실행 중 선택, Shift/Ctrl·창 선택 및 편집 범위의 스냅은 원래 박스 기준이다. Limited/Unlimited는 박스 높이 안에서 동일하게 굽히므로, 두 모드의 차이는 박스 밖에 있는 입력 형상으로 확인한다.

박스의 직교 구조가 손상되면 오류가 표시된다. Undo하거나 해당 박스를 Remove한 뒤 새 Control Box를 추가한다. 곡률 중심에 닿는 붕괴 및 한 바퀴 이상 감기는 입력은 계산하지 않는다. 원본/박스 이동 중 계산 실패는 마지막 유효 결과와 오류 상태로 표시한다. 현재 입력에서 실패하는 Strength 제안은 적용 전에 거부한다.

## 여러 박스와 전체 이동

1. Bend를 다시 선택하고 Control Box를 하나 더 추가한다. 새 박스는 다른 박스가 굽히기 전의 Bend 입력 결과에 맞춰진다.
2. 두 박스의 위치·회전·Strength를 다르게 설정한다. 이름도 바꿔 구분한다.
3. 트리에서 두 박스의 순서를 바꾼다. 위 박스부터 이전 결과에 적용되어 순서에 따라 결과가 달라져야 한다. 각 Control Box 표시는 자기 Strength만 반영하고 다른 박스에 의해 다시 굽어지면 안 된다.
4. 다른 Bend로 박스를 옮기는 것은 허용되고 루트·Boolean·Mirror·Array로 옮기는 것은 거부되어야 한다. 박스 사이에 일반 입력 행이 있어도 박스들의 상대 순서로 계산한다.
5. 내부 편집을 나와 최종 결과를 한 번 클릭해 전체 Bend를 선택한 뒤 이동·회전한다. 모든 입력과 박스가 같이 움직이고 상대적인 굽힘이 유지되어야 한다.
6. 전체 결과 더블클릭으로 내부 편집에 들어가 Control Box 또는 입력 하나만 선택해 조작한다. 다른 입력·제어 객체가 같이 움직이지 않아야 한다.
7. 일반 작업에서는 숨겨진 원본과 박스에 스냅이 걸리지 않고 최종 결과에만 스냅해야 한다. 편집 범위에서는 해당 단계의 객체를 선택·스냅할 수 있다.

여러 박스 합성 후 원래 형상의 모든 길이가 유지된다는 뜻은 아니다. 기준 설명은 [형상 계산 검토](Bend_Feasibility_Study.md)에 있다.

## 저장·Undo·복제·Bake/Merge

- 저장하고 Rhino를 재시작해 연다. Bend 이름, 박스 순서·Strength·Mode·위치·방향·크기, 입력 GUID 연결과 결과가 유지되어야 한다.
- Add Control Box, Fit, 이름+Strength 변경, 순서 변경, 개별 이동, 전체 이동을 각각 Undo/Redo한다. 한 동작당 한 번으로 복원되고 결과가 겹쳐 생기지 않아야 한다.
- **Enabled OFF**는 첫 일반 입력만 전달하고 박스·트리는 유지한다. 다시 ON으로 하면 Bend가 재계산된다.
- **Duplicate subtree**는 모든 박스와 입력을 별도 객체로 복제한다. 복제본 박스를 움직이거나 값을 바꿔도 원본 Bend는 바뀌지 않아야 한다.
- **Bake**는 트리를 유지하고 굽힌 고정 결과만 루트에 추가한다. **Merge**는 선택한 Bend 서브트리를 같은 위치의 고정 결과 하나로 바꾸고 기존 입력·박스를 제자리에 숨긴다.
- Bake/Merge 결과에는 Control Box가 포함되지 않고, 생성된 결과를 움직여도 기존 박스가 따라오지 않아야 한다.
- Control Box만 **Remove**하면 그 박스만 제자리에서 숨기고 등록을 제거한다. Bend를 Remove하면 제어 박스들은 숨기고 일반 자식만 승격한다. Undo 시 같은 위치와 원래 구조로 돌아와야 한다.

## 개발 검사

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-rhino.ps1 -Configuration Release -BendOnly
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-rhino.ps1 -Configuration Release -UIOnly
```

0.15.1 자동 검사: **Core 205 + Rhino·Eto/WPF 772 = 977 PASS**, 기존 headless Undo/Redo 제약 3 SKIP. 빌드 경고·오류 0개. 새 cage 형상·변환·선택 검사 30개와 캐시 검사 23개를 포함한다. Show result OFF의 MTreeMove 표시 보완 후 최종 빌드와 전체 재검사도 통과했다. 실제 이동 화면은 위 수동 확인 대상이다.

[최종 Release 빌드 로그](../artifacts/validation/0.15.1/release-build-2026-09-11.txt), [1차 전체 Rhino 검사 로그](../artifacts/validation/0.15.1/rhino-checks-2026-09-11.txt), [최종 재검사 로그 발췌](../artifacts/validation/0.15.1/rhino-recheck-excerpt-2026-09-11.txt). 최종 재검사 출력은 크기 제한으로 일부 생략되었으며 종료 코드는 0이다.
