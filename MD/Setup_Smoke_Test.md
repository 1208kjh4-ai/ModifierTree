# 세팅 단계의 Rhino 수동 검증

자동 빌드 및 Core 검증과 별도로 실제 Rhino에서 수행할 절차입니다.
아래 체크박스는 전체 수동 검증 절차입니다. 사용자가 제공한 화면과 로그에서 확인된 범위는
[Transform 로그 분석](Transform_Trace_Findings.md)에 별도로 기록합니다.

## 플러그인과 등록

- [ ] 새 테스트 문서에서 .rhp 로드 → `MTreeStatus` 정상 출력, .NET 8 확인.
- [ ] `MTree` → 도킹 패널 열기, 닫기, 다시 열기.
- [ ] 겹치는 Box 두 개를 생성하고 `Mass_01`, `Mass_02`로 이름 지정.
- [ ] 두 객체를 등록 → 원본 수와 GUID가 그대로이며 패널에 두 항목 표시.
- [ ] 같은 객체 재등록 → 항목 중복 없음.
- [ ] Add Object 선택 도중 Esc → 등록 개수 변화 없음.
- [ ] Rhino Properties에서 이름 변경 → 패널 이름 갱신.
- [ ] 같은 이름을 두 객체에 지정 → 서로 다른 두 항목 유지.
- [ ] Remove from Modifier → 해당 항목만 제거, Rhino 객체 유지.
- [ ] 다시 등록 후 원본 Delete → Missing 표시, Undo → Registered 복구.
- [ ] 문서 닫고 새 문서 열기 → 이전 등록이 남지 않음.

## 이벤트 관찰

`MTreeTrace`로 기록을 켜고 아래 동작을 하나씩 수행합니다.
각 시나리오 후 `MTreeDumpLog`의 기록에서 순서와 ID를 확인합니다.
최종 Geometry와 Tree 동기화의 성공 여부를 검증하는 단계는 아닙니다.

| 시나리오 | 확인할 정보 |
|---|---|
| Move | Before/After ID 연결, 변환 행렬, Replace/Add/Delete 순서 |
| Rotate | 회전 행렬과 객체 ID 유지/변경 |
| Scale, Scale1D | 균일·비균일 스케일의 이벤트 차이 |
| Gumball 이동·회전·스케일 | 드래그 중과 확정 시의 이벤트 발생 시점 |
| 명령 취소, Gumball Esc | Before만 발생하는지, 실제 객체 교체 여부 |
| Copy (별도 명령, 미검증) | Transform 이벤트 발생 여부와 copy 값, 원본과 복사본 GUID 구분 |
| Undo → Redo | Undo/Redo 플래그와 복원 객체 GUID |
| 여러 객체 동시 이동 | 하나의 Transform에 포함된 객체 목록 |

기록은 문서를 닫기 전에 F2 명령 기록에서 복사해야 합니다.
이벤트만으로 의도를 확정하지 않고, 실제 객체 위치 및 작업 결과와 함께 판단합니다.

## 다음 프로토타입의 완료 기준

1. 등록된 Box A와 B에서 Difference 결과를 별도 관리 객체로 표시.
2. B를 Move → 결과 재계산.
3. 결과를 Move → A와 B에 한 번씩 동일한 변환 적용.
4. 다시 B를 Move → 이동된 배치에서 결과 정상 갱신.
5. Rotate/Scale/Gumball 및 Undo/취소에서 원본과 결과 불일치가 없는지 확인.

위 기준은 이번 세팅 단계에서 구현·검증된 것으로 간주하지 않습니다.
