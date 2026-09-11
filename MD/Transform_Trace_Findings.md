# Transform 이벤트 관찰 결과 — 2026-09-07

사용자가 제공한 Rhino 화면과 명령 로그를 분석한 결과입니다. 플러그인이 직접 모델을 변환하거나
Boolean 결과를 갱신하는 동작까지 검증한 것은 아닙니다.

## 입력과 범위

- 화면: Modifier Manager 로드, `Brep A`, `Brep B` 두 Registered 항목 표시.
- 첫 로그: 26개 이벤트, Drag 두 번과 Undo/Redo.
- 두 번째 로그: 이전 26개를 포함한 누적 72개 이벤트. 새 관찰 구간은 sequence 27 이후.
- 대상 객체 GUID: `7f663455-736e-49f6-87c8-4588d6fd011c`.
- 명령 기록상 선택 객체는 closed extrusion. 표시 이름의 Brep가 실제 객체 타입을 보장하지 않음.

## 관찰

| sequence | 작업 | 결과 |
|---|---|---|
| 28–34 | Move | X 방향 약 -18,937.199 모델 단위 이동. transformId=9, copy=false, Success |
| 35–41 | Rotate | Z축 방향 30도 회전에 해당하는 행렬. transformId=10, copy=false, Success |
| 42–48 | Scale1D | Z 방향 약 1.731322배 스케일과 기준점 보정 이동. transformId=11, Success |
| 49–53 | Undo | Scale1D 이전 runtime serial 17517 복원. Transform 이벤트 없음 |
| 54–58 | Redo | Scale1D 이후 runtime serial 17985 복원. Transform 이벤트 없음 |
| 59–60 | Move 중단 | BeginCommand → EndCommand(result=Nothing). 변환·교체 이벤트 없음 |
| 61–67 | 마지막 Move | X 방향 약 +14,529.591 이동. transformId=12, copy=false, Success. 복제 테스트로 취급하지 않음 |

완료된 Move / Rotate / Scale1D는 모두 다음 순서였습니다.

```text
BeginCommand
BeforeTransform
ReplaceObject
DeleteObject
AddObject
AfterTransform
EndCommand (Success)
```

이 구간에서 GUID는 유지됐고 runtime serial만 변경됐습니다. Before/After의 transformId가 일치합니다.
회전·스케일 행렬의 이동 성분에는 기준점을 반영한 보정이 포함되므로, 향후 전파 시 전체 4×4 행렬을 사용해야 합니다.

Undo/Redo는 Replace → Delete → Undelete 순서였습니다. EndCommand 시점에도 각각 undo/redo 플래그가 true였습니다.
Move 중단은 이 시도에서 `Cancel`이 아닌 `Nothing`으로 기록됐습니다. 이 관찰을 모든 취소 경로로 일반화하지 않습니다.

## 구현에 반영할 사항

1. 원본은 GUID로 다시 조회하고 RhinoObject 인스턴스를 장기간 보관하지 않음.
2. Replace 과정의 Delete를 등록 해제로 처리하지 않음. 작업 완료 후 존재 여부 재확인.
3. 변경 이벤트에서 재계산 필요 상태만 기록하고 명령 및 Undo/Redo 처리가 끝난 뒤 병합하여 계산.
4. Undo/Redo는 변환 행렬 없이 현재 원본 Geometry를 재조회하여 결과 갱신.
5. 취소 여부를 EndCommand의 Cancel 값만으로 판단하지 않음. 실제 원본 변경 여부를 함께 사용.
6. Boolean 입력은 Extrusion을 Brep로 변환할 수 있어야 함.

## 정정과 미검증 항목

이전 안내의 `Move Copy=Yes`는 잘못된 옵션입니다. 복제는 별도 `Copy` 명령으로 관찰할 수 있습니다.
이번에는 복제를 실행하지 않았으므로 copy=true 경로와 복사본의 GUID/등록 정책은 미검증입니다.

또한 Root/Subtree 결과 이동, 여러 조각의 결과, 이벤트 재진입, 자동 재계산과 Rhino Undo의 결합은
아직 검증되지 않았습니다. 현재 증거는 원본 변경 감지를 이용한 Difference 프로토타입을 시작할 근거입니다.

공식 명령 참고: [Move](https://docs.mcneel.com/rhino/8/help/en-us/commands/move.htm),
[Copy](https://docs.mcneel.com/rhino/8/help/en-us/commands/copy.htm).
