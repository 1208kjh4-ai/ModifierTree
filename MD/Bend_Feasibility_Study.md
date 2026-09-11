# Bend 형상 계산 검토 — 2026-09-11

후속 상태: 아래는 최초 프로토타입 검토 기록이다. 이후 0.15.0에서 Bend·Control Box·트리·저장·Undo를 본체에 연결했다. 현재 사용법과 검증 범위는 [Bend 화면 확인](Bend_Smoke_Test.md)을 참고한다.

단일 Control Box의 Brep 변형 가능성을 검증했다. 현재 제안은 **로컬 Y 중립축의 길이를 보존하는 자체 좌표 변형식 + Rhino `SpaceMorph`의 NURBS/Brep 재근사**다. 아래 실험 범위에서는 닫힌 솔리드와 후속 Boolean이 유지됐다. 제품의 Bend 노드, UI, 저장, Undo 연결은 다음 단계이며 설치 버전은 0.14.2다.

## 이번에 정리한 동작

- Strength는 굽힘량이며, 방향은 Control Box 회전으로 지정한다. 별도 Angle UI는 두지 않는다.
- Limited는 박스의 로컬 Y 높이 구간에서 굽힌다. 아래쪽은 그대로 두고, 위쪽은 끝 단면의 회전·이동을 따라 직선으로 이어진다.
- Unlimited는 박스 높이를 벗어난 Y 구간에도 같은 곡률을 적용한다.
- Within Box가 없으므로 박스 X/Z 폭 밖의 형상을 변형에서 제외하지 않는다. 이 수식에서는 X/Z 폭은 제어 표시·선택 크기, Y 높이는 곡률 결정에 쓰인다.
- 여러 Control Box는 트리 순서대로 이전 결과에 적용한다. 제어 박스 자체는 서로 구부리지 않는다.
- Keep Y-Axis Length는 각 박스의 **로컬 Y 중립축 호길이**를 기준으로 항상 적용한다. 두께의 모든 모서리 길이나 여러 박스 합성 후 모든 경로의 길이를 보존하는 뜻은 아니다.

Limited/Unlimited의 범위는 [Maxon Bend 공식 문서](https://help.maxon.net/c4d/r21/us/html/OBEND-ID_OBJECTPROPERTIES.html)의 설명을 참고했다. 아래 수식은 합의한 동작에 맞춰 자체 유도했으며 Cinema 4D 내부 알고리즘과의 완전한 일치를 주장하지 않는다.

## 계산 방식

이번 프로토타입에서는 박스 하단 중심을 로컬 원점으로 사용한다. 높이 `L > 0`, Strength `θ` 라디안, 곡률 `k = θ/L`로 두고 +Y 방향 선을 +X 쪽으로 굽힌다. 향후 화면의 박스 중심 좌표를 하단 프레임으로 변환할 수 있다.

```text
a = k * y
X = (1 - cos(a)) / k + x * cos(a)
Y = sin(a) / k - x * sin(a)
Z = z
```

`k=0`은 항등 변형이다. 코드에서는 작은 Strength에서 상쇄 오차를 줄이도록 `1-cos(a)` 대신 `2*sin(a/2)^2`를 쓴다. Limited는 y를 `[0,L]`로 제한한 뒤 남는 길이를 해당 끝단의 접선 방향으로 더한다.

중립축 `x=0`에서는 변형 후 접선 길이가 1이므로 원래 직선 길이와 변형된 원호 길이가 같다. 예를 들어 높이 100mm, Strength 90°이면 반지름은 `100/(π/2) ≈ 63.661977mm`다. 세계 좌표의 Y 방향 높이가 100mm로 남는다는 뜻은 아니다.

형상은 원본을 복제해서 변형한다. `PreserveStructure=false`, `QuickPreview=false`로 NURBS 재근사를 허용한다. `PreserveStructure=true`는 제어점 구조 유지 옵션이며 길이 유지 옵션이 아니다. Rhino 명령의 재근사 설명은 [Rhino Bend 공식 도움말](https://docs.mcneel.com/rhino/8/help/en-us/commands/bend.htm), API 사용 형태는 [공식 Space Morph 예제](https://developer.rhino3d.com/en/samples/rhinocommon/space-morph/)를 참고했다. 상세 속성 설명은 설치된 `RhinoCommon.xml`도 대조했다.

## 측정 결과

환경: RhinoCommon **8.35.26251.13001**, .NET 8, 단위 mm. 문서 공차 0.001mm, 변형 근사 공차 0.0001mm. `/safemode`, 별도 `/scheme=ModifierTreeBendProbe`, `WindowStyle.NoWindow`로 실행했다. 실행 중인 사용자 Rhino나 설치 플러그인을 조작하지 않았다.

**프로토타입 빌드: 경고 0, 오류 0. 최종 검사: 82 PASS, 0 FAIL.** 제품 전체 회귀 검사와 별도 집계다.

| 항목 | 결과 |
|---|---|
| 박스, 원통, 관통 구멍이 있는 박스, Y 경계를 가로지르는 박스 | 각 0/45/90/-90/180° × 두 모드, 총 40개 사례 성공 |
| 닫힘·유효성 | 40개 모두 Morph 성공, IsValid/IsSolid, 열린 모서리 0, 바깥 방향, 양의 체적 |
| 변형 정확도 | 각 원본 trimmed face의 12×12 샘플 및 경계 주변 점에서 이론 위치→결과 Brep 거리 최대 약 **0.0006293mm** |
| 원본 보존 | 40개 모두 입력 DataCRC 불변 |
| 중립축 길이 | 150mm 직선에 7개 Strength × 두 모드 적용, 실제 재근사 NURBS 곡선 길이 오차 최대 약 **0.00009394mm** |
| Limited/Unlimited | 아래 고정/변형 구분, 위 직선 접선 연장/추가 굽힘, 90° 끝점 확인 |
| 굽힌 결과의 Difference | 관통 Cutter로 1개 닫힌 솔리드, 체적 약 7999.9981 → 7899.4672mm³ |
| 굽힌 결과의 Union | 겹친 구와 1개 닫힌 솔리드, 체적 약 7999.9981 → 8261.7968mm³ |
| 두 박스의 AB/BA 순차 변형 | 두 결과 모두 유효한 닫힌 Brep, 합성식 대비 샘플 편차 각각 약 0.00002045/0.00003920mm |
| 전체 이동·회전 | 프레임과 입력을 같이 강체 변환한 점의 계산 결과 일치, 최대 오차 약 3.3e-14mm |
| 독립 파일 저장 | 결과 Brep 8개를 3dm에 쓰고 다시 읽어 개수·유효성·닫힘 확인 |

샘플 편차는 유한 지점의 측정이며 전체 곡면의 최대 오차를 증명하지 않는다. `IsValid/IsSolid` 역시 모든 자기교차를 배제하는 보장은 아니다. 전체 이동·회전은 프레임 수식의 점 검증이며 실제 검볼 이벤트 검사가 아니다.

### 근사 공차를 조정한 이유

처음에는 문서 공차와 같은 0.001mm를 사용했다. 높이 100mm 박스에서 Unlimited ±180°를 길이 150mm 선 전체에 적용하면 총 270°가 되는데, 재근사 후 길이가 **149.998548678mm**였다. 길이 오차 0.001451322mm가 목표 0.001mm를 넘었다.

근사 공차를 0.0001mm로 줄인 뒤 같은 선은 **149.999906067mm**가 되어 이번 기준을 통과했다. 곡면의 위치 오차와 곡선의 적분 길이 오차는 같은 값이 아니다. 공차 1/10 정책은 이번 실험에서 유효했던 후보이며 임의 형상·스케일에 대한 보장은 아니다. 첫 측정은 [기존 공차 로그](../artifacts/bend-probe/baseline-tolerance-0.001/run.log)에 보관했다.

### 여러 박스의 길이 유지 한계

같은 위치·방향에서 Strength 45° 박스를 두 번 적용했다.

```text
원본 중립축 길이         100mm
두 박스 합성식 길이      93.421546mm
실제 재근사 곡선 길이    93.421510mm
```

이는 근사 오차가 아니라 공간 변형을 합성했을 때 생기는 변화다. 두 번째 박스에서 이미 구부러진 곡선은 그 박스의 중립축 위에 있지 않다. **각 박스의 중립축 길이 유지**를 채택하면 현재 모델을 사용할 수 있다. **여러 박스를 적용한 뒤에도 동일한 중심 경로의 총길이를 반드시 보존**하려면 중심선 추적과 프레임 운반을 포함한 별도 모델을 먼저 설계해야 한다.

### 속도 관찰

90° Limited를 한 번 적용하는 Morph 호출만 측정했다. 박스 약 4.1ms, 관통 구멍 박스 약 10.2ms, 경계 통과 박스 약 14.4ms, 원통 약 46.5ms였다. 반복 벤치마크 중앙값이 아니며 복제·검증·Boolean·캐시·뷰포트 갱신 시간은 제외된다. 이 값을 실제 프리뷰 갱신 속도로 해석하면 안 된다.

## Rhino BendSpaceMorph와 비교

Rhino 기본 API도 닫힌 박스 변형에 성공했고, 중립축 길이를 유지했다. 다만 이번 설치 버전에서 입력 끝점과 Strength의 조합을 정확히 구성해야 했다.

- spine `(0,0,0)→(0,100,0)`, direction point `(100,100,0)`, angle `π/2`를 전달하면 중립축 끝점은 약 `(45.969769,84.147098,0)`이었다.
- direction point를 길이 보존 원호의 끝점 `(63.661977,63.661977,0)`으로 구성하면 자체 식의 90° 중립축과 일치했다.
- 이번 네 조합에서는 `straight=true/false`를 바꿔도 동일한 점 결과를 얻었고, 아래쪽은 고정됐다. 이 불리언을 그대로 우리가 정한 Unlimited로 연결할 근거는 얻지 못했다.

이것은 제한된 입력 조합의 관찰이며 API의 모든 옵션이 무효라는 뜻이 아니다. **동일한 정의로 Limited/Unlimited를 제공하기 위해 자체 SpaceMorph를 다음 구현의 기본 후보로 권장한다.** 기본 API는 Limited에 대한 추가 비교·최적화 후보로 남길 수 있다.

## 다음 구현 전에 필요한 경계 처리

이 수식의 내부 구간에서 야코비안 행렬식은 `1-k*x`다. 0이면 Y 방향이 곡률 중심으로 붕괴하고, 음수이면 뒤집힌다. 실제 입력의 로컬 X 범위를 검사해야 하며, 표시 박스의 폭만 검사하면 안 된다. Limited의 외부 강체 구간은 별도로 취급해야 한다.

이번 프로토타입은 얇은 형상과 제한된 Strength를 사용했으며, 이 붕괴 검사를 제품용 오류 처리로 구현하지 않았다. 두꺼운 형상, 360° 이상의 감김, 긴 꼬리끼리 충돌하는 경우, 내부 공동·분리된 복합 Brep·여러 박스의 자기교차는 추가 검증 대상이다. 위험한 입력에서는 이전 유효 결과를 보존하는 제품의 실패 경로와 연결해야 한다.

Limited의 경계에서는 위치와 접선 방향이 이어지지만, 중립축 밖에서 Y 방향 변화율은 달라진다. 경계를 가로지르는 복잡한 trimmed face의 근사·공차 축적도 다음 단계에서 확인해야 한다.

이번 검사는 production `ResultGeometry`, `ResultCommit`, `WorkingResultObjects`를 호출하지 않았다. 따라서 **Bake/Merge UI, 작업용 결과의 선택·스냅, 캐시, 저장된 Bend 트리, Undo까지 검증한 것은 아니다.** 닫힌 Brep과 후속 Boolean, 독립 3dm 저장이라는 형상 전제까지 확인했다.

다음 구현 순서는 Bend/ControlBox 데이터 모델 및 여러 제어 객체의 저장 → 평가기·실패 보존 → 전체/개별 이동과 Undo → 패널·검볼 → Bake/Merge 제어 객체 제외와 기존 기능 회귀 검증이다.

## 파일과 재실행

| 파일 | 역할 |
|---|---|
| [LengthPreservingBend.cs](../prototypes/ModifierTree.Bend.Probe/LengthPreservingBend.cs) | 로컬 프레임과 Limited/Unlimited 변형식 |
| [BendProbe.cs](../prototypes/ModifierTree.Bend.Probe/BendProbe.cs) | 실제 Brep·곡선·Boolean·순차 적용 검사 |
| [Program.cs](../prototypes/ModifierTree.Bend.Probe/Program.cs) | 격리된 창 없는 Rhino 호스트 |
| [probe-bend.ps1](../scripts/probe-bend.ps1) | 별도 프로토타입 빌드·실행 |
| [measurements.json](../artifacts/bend-probe/measurements.json) | 측정 수치 |
| [run.log](../artifacts/bend-probe/run.log) | 최종 82개 검사 로그 |
| [bend-samples.3dm](../artifacts/bend-probe/bend-samples.3dm) | 플러그인 없이 열 수 있는 고정 결과 8개 |

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\probe-bend.ps1
# RhinoCore를 시작하지 않고 프로토타입만 빌드:
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\probe-bend.ps1 -BuildOnly
```

실행에는 설치된 Rhino 8과 해당 호스트가 사용할 수 있는 라이선스가 필요하다. 이 프로토타입은 제품 솔루션·설치 스크립트에 포함하지 않았다.

샘플 파일은 World XY 기준 낮은 Y 행이 Limited, 높은 Y 행이 Unlimited이며 각 행은 왼쪽부터 박스·원통·관통 구멍·경계 통과 박스다. 모두 +90° 결과이고 Rhino 객체 이름에 모드·각도·형상이 표시된다. 파일은 현재 트리와 연결되지 않은 검토용 고정 Brep이다.
