# 0.14.1 — 이동 후 Undo의 결과 중복 수정

2026-09-11 사용자가 결과를 한 번 이동한 뒤 Undo 등에서 각각 선택 가능한 결과 두 개가 겹치는 현상을 보고했다.

## 확인한 원인

별도 설정의 NoWindow Rhino에서 Move → Undo 기록 밖의 결과 Replace → Undo를 실행하면 두 객체가 남는 것을 재현했다. 현재 작업용 결과가 기존 GUID를 차지하므로 Rhino가 이전 native 객체를 새로운 GUID로 복원한다. 기존 객체의 runtime serial은 유지된다. 재계산 후 현재 결과까지 원래 위치로 돌아가면 두 결과가 겹친다.

GUID만 기록했던 0.14.0은 이 새 GUID를 관리 대상에서 놓쳤다. 별개로 AddBrep가 요청과 다른 GUID를 반환하면 생성 성공을 실패로 취급하며 실제 생성된 객체를 놓치는 경로도 있었다. 요청 GUID 충돌 시 재할당은 [Rhino ObjectId 공식 API](https://developer.rhino3d.com/api/rhinocommon/rhino.docobjects.objectattributes/objectid)에 명시되어 있다. Undo에서의 serial 유지와 GUID 변경은 [native 재현 기록](../artifacts/probes/WorkingIdentity0141/Findings.md), [원문 출력](../artifacts/probes/WorkingIdentity0141/stdout.log)에서 확인했다.

## 수정

- 생성 API가 실제 반환한 GUID를 관리한다.
- 생성·교체 전후의 runtime serial과 과거 GUID를 세션 동안 보존한다. Undo/Redo로 새 GUID가 생겨도 해당 native 객체를 소유 기록과 연결한다.
- 동기화 시 Modifier별 결과를 하나로 정리하고, 제거되는 중복의 선택 상태를 남는 결과로 옮긴다.
- Undo/Redo 명령이 끝나면 원본 기준 재계산과 결과 동기화를 예약한다. Undo 콜백 도중 native 객체를 수정하지 않는다.
- 저장 준비는 현재 결과뿐 아니라 되살아난 과거 결과도 제거한다. 서로 다른 serial을 가진 일반 Copy와 원본은 보존한다.

## 검증

Release 빌드 경고·오류 0개. **Core 172 + Rhino 608 = 780 PASS**, 기존 호스트 제약 3 SKIP. [빌드 로그](../artifacts/validation/0.14.1/release-build-2026-09-11.txt), [Rhino 검사 로그](../artifacts/validation/0.14.1/rhino-checks-2026-09-11.txt).

이번 회귀 검사 17개에는 실제 GUID 충돌, 반복 동기화, 과거 객체 복원, 새 GUID와 기존 serial 인식, 선택 승계, 일반 Copy 보존, 저장 전 정리를 포함한다. 수정 전 현상은 native Undo로 재현했다. 수정 후 정리 검사는 native Undelete로 같은 객체 복원을 재현한다. 창 없는 호스트에서 직접 Undo API 호출 후 UndoActive가 유지되므로, 실제 Rhino UI의 연속 Undo/Redo까지 완료한 것으로 보지 않는다. 이번 검증은 모두 창 없는 호스트로 수행했다.

## 설치 후 확인

1. Rhino 작업을 저장하고 종료한 뒤 `scripts/install-plugin.ps1`을 실행한다. 재시작 후 `MTreeStatus`가 `0.14.1.0`인지 확인한다.
2. 중복이 없는 상태에서 최종 결과를 한 번 Move하고 Undo한다. 원래 위치에 결과가 하나만 남으며 선택·스냅되는지 확인한다.
3. Move → Rotate → Undo → Undo → Redo → Redo를 반복한다. 결과가 늘어나지 않고 원본과 Array 축·BasePlane이 함께 복원되는지 확인한다.
4. 결과를 Copy한 뒤 Undo/Redo한다. 의도한 고정 복사본을 중복 정리가 제거하지 않아야 한다.
5. 내부 편집 진입·복귀, Show result OFF/ON, 저장·다시 열기 이후에도 결과 수가 유지되는지 확인한다.

## 이미 중복된 파일

0.14.0에서 유출된 중복을 파일에 저장하고 새 세션으로 열면 과거 runtime serial 소유 기록은 남아 있지 않다. 이번 수정은 그 객체를 자동 삭제하지 않는다. 동일한 사용자 문자열이 일반 Copy에도 남아 있을 수 있으므로 문자열이나 모양이 같다는 이유만으로 일괄 삭제하면 안 된다. 기존 중복 파일 정리는 해당 객체를 확인한 뒤 별도로 수행해야 한다.
