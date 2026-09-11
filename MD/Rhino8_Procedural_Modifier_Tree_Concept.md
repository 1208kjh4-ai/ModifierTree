# Rhino 8 Procedural Modifier Tree — Concept / Feasibility Prototype Spec

## 0. 문서 목적

이 문서는 Rhino 8에서 Cinema 4D의 Object Manager / Modifier 개념 일부를 참고한 **비파괴형(Non-destructive) Procedural Modifier Tree**의 초기 아이디어를 Codex에게 전달하기 위한 구현 검토 문서다.

현재 단계의 목표는 완성형 플러그인을 만드는 것이 아니라 다음을 검증하는 것이다.

1. Rhino 객체를 사용자가 선택적으로 Modifier 시스템에 등록할 수 있는가.
2. 등록된 Geometry와 Modifier를 트리 구조로 관리할 수 있는가.
3. 하위 노드의 결과를 상위 Modifier의 입력으로 연쇄 계산할 수 있는가.
4. Boolean / Bend 같은 기본 Modifier를 비파괴적으로 재계산할 수 있는가.
5. Rhino Viewport에서 객체를 Move / Rotate / Scale했을 때 Tree 내부 관계를 안정적으로 유지할 수 있는가.
6. 이 방식이 Grasshopper와 별개의 "Object-based procedural modeling" UX로 실제 사용할 가치가 있는가.

---

# 1. 핵심 개념

Grasshopper처럼 자유로운 노드 그래프를 만드는 것이 목적이 아니다.

목표는 Rhino 일반 모델링과 Grasshopper 사이에 다음과 같은 중간 계층을 만드는 것이다.

```text
Rhino Direct Modeling
        ↓
Procedural Modifier Tree
        ↓
Grasshopper
```

Modifier Tree는 **Object 중심**으로 동작해야 한다.

사용자는 Rhino에서 평소처럼 Geometry를 만들고, 필요한 객체만 Modifier Manager에 등록한 뒤, Tree에서 Geometry와 Modifier의 관계를 구성한다.

---

# 2. 기본 UI 개념

Cinema 4D Object Manager와 유사한 계층형 패널을 Rhino Dockable Panel로 구현한다.

예:

```text
MODIFIER MANAGER

[ + Add Object ]   [ + Modifier ]

▼ Bend
   ├─ ▼ Boolean Difference
   │    ├─ Mass_01
   │    └─ Mass_02
   │
   └─ Base_Curve
```

이 Tree는 단순 분류용이 아니라 **실제 계산 구조**다.

위 예시는 다음으로 해석한다.

```text
BooleanResult = Mass_01 - Mass_02

FinalResult = Bend(
    BooleanResult,
    Base_Curve
)
```

즉 계산은 **Child → Parent 방향**으로 이루어진다.

---

# 3. Node 종류

초기 설계에서 최소한 다음 세 종류를 구분한다.

## 3.1 Geometry Node

실제 Rhino Document 객체를 참조한다.

예:

```text
Mass_01
Mass_02
Base_Curve
```

가능한 Geometry 예:

- Brep
- Extrusion
- Surface
- Curve
- Mesh

초기 MVP에서는 Brep / Curve 중심으로 제한해도 된다.

---

## 3.2 Operator / Modifier Node

하위 노드의 결과를 입력받아 새로운 Geometry 결과를 만든다.

초기 Prototype에서는 Boolean 계열만 구현한다.

지원 대상:

```text
Boolean Union
Boolean Difference
Boolean Intersection
```

Bend, Taper, Twist, Extrude 등은 **초기 단계에서 제외**한다.

초기 목적은 Modifier 종류를 늘리는 것이 아니라 다음 핵심 엔진을 검증하는 것이다.

- Rhino Object 등록
- Tree 구조
- Child 순서 기반 입력 해석
- Operator 결과 재계산
- Nested Boolean
- Rhino Viewport Transform 동기화
- Result / Source 객체 관리
- 실패 처리 및 Dependency 갱신


---

## 3.3 Container Node

선택 사항.

Geometry를 직접 생성하지 않고 Tree 정리 / 그룹 / 부모 Transform을 담당한다.

예:

```text
Building
Tower
Podium
Group
Folder
```

MVP 단계에서는 생략 가능하다.

---

# 4. Geometry 이름 규칙

Geometry Node의 표시 이름은 **Rhino Object Name**을 따른다.

예를 들어 Rhino Properties에서:

```text
Name = Mass_01
```

이면 Modifier Manager에도:

```text
Mass_01
```

로 표시한다.

중요:

- 실제 객체 식별은 절대로 Name으로 하지 않는다.
- 내부 연결은 반드시 Rhino Object GUID를 사용한다.
- 동일한 Object Name이 여러 개 존재할 수 있기 때문이다.

개념적으로:

```text
DisplayName = RhinoObject.Attributes.Name
ObjectId    = RhinoObject.Id
```

Rhino에서 이름을 바꾸면 Modifier Manager도 즉시 갱신한다.

가능하다면 Modifier Manager에서 Rename했을 때 Rhino Object Name도 변경하여 양방향 동기화한다.

이름이 없는 객체는 Manager에서 임시 표시 이름을 사용해도 된다.

예:

```text
Brep_01
Curve_01
```

단, 자동 표시 이름 때문에 Rhino Object Name을 강제로 수정할 필요는 없다.

---

# 5. Add Object 시스템

Modifier Manager에 Rhino Document의 모든 객체를 자동으로 표시하지 않는다.

이 시스템에 참여하는 객체만 사용자가 명시적으로 등록한다.

패널:

```text
[ + Add Object ]
```

동작:

```text
Add Object 클릭
    ↓
Rhino Viewport에서 객체 선택
    ↓
Enter
    ↓
선택한 객체의 GUID를 Modifier Registry에 등록
    ↓
Tree Root에 Geometry Node 생성
```

복수 선택을 지원하는 것이 좋다.

예:

```text
Mass_01
Mass_02
Base_Curve
```

세 개를 한 번에 등록할 수 있어야 한다.

---

# 6. Add Object는 객체 복사가 아니다

`Add Object`는 Rhino 객체를 복사하거나 새 Geometry를 만드는 기능이 아니다.

기존 Rhino Object를 Modifier 시스템에 **Register**하는 기능이다.

개념:

```text
Rhino Object
    ↓
GUID Reference
    ↓
Modifier Registry
```

Rhino Document가 전체 모델을 관리하고, Modifier Manager는 등록된 일부 객체의 procedural 관계만 관리한다.

---

# 7. Remove와 Delete를 구분

Modifier Manager에서 객체를 제거하는 것과 Rhino 객체를 삭제하는 것을 반드시 구분한다.

예:

```text
Right Click

Rename
Add Modifier
----------------
Remove from Modifier
Delete Rhino Object
```

### Remove from Modifier

- Modifier Tree / Registry에서 제거
- Rhino Object는 유지

### Delete Rhino Object

- Modifier Tree에서 제거
- Rhino Document에서도 삭제

이미 다른 Modifier에 사용 중인 객체를 Remove하려 할 경우 경고가 필요하다.

예:

```text
Mass_02 is used by:
Boolean Difference

[ Cancel ]
[ Remove and Disconnect ]
```

---

# 8. Tree 연산 기본 규칙

가장 중요한 공통 규칙:

```text
Parent = Operator
Child  = Input
```

계산 방향:

```text
Child Result
    ↓
Parent Operator
    ↓
Parent Result
```

즉 아래에서 위로 계산한다.

예:

```text
▼ Taper
   └─ ▼ Bend
        ├─ ▼ Boolean Difference
        │    ├─ Building_Mass
        │    ├─ Courtyard
        │    └─ Entrance_Void
        │
        └─ Bend_Base
```

개념적인 계산:

```text
BooleanResult =
    Building_Mass
    - Courtyard
    - Entrance_Void

BendResult =
    Bend(BooleanResult, Bend_Base)

FinalResult =
    Taper(BendResult)
```

---

# 9. Boolean 입력 순서 규칙

Boolean에서는 별도의 A / B 슬롯 UI를 만들지 않는 방향을 우선 검토한다.

대신 **자식 순서 자체가 연산 의미를 가진다.**

## Boolean Difference

첫 번째 자식:

```text
A
```

두 번째 이후:

```text
B / Cutter
```

예:

```text
▼ Boolean Difference
   ├─ Mass_01
   └─ Mass_02
```

해석:

```text
Mass_01 - Mass_02
```

순서를 Drag & Drop으로 바꾸면:

```text
▼ Boolean Difference
   ├─ Mass_02
   └─ Mass_01
```

해석:

```text
Mass_02 - Mass_01
```

여러 Cutter:

```text
▼ Boolean Difference
   ├─ Building_Mass
   ├─ Courtyard
   ├─ Core_Void
   └─ Entrance_Void
```

해석:

```text
Building_Mass
-
(Courtyard + Core_Void + Entrance_Void)
```

실제 구현에서는 BooleanDifference의 API 특성에 따라 cutters를 배열로 전달할 수 있다.

---

# 10. Modifier 결과도 다른 Modifier의 입력이 되어야 한다

중요한 요구사항.

Modifier는 최종 결과만 만드는 것이 아니라 그 결과가 다시 상위 Modifier의 Input으로 사용될 수 있어야 한다.

예:

```text
▼ Bend
   ├─ ▼ Boolean Difference
   │    ├─ Mass_01
   │    └─ Mass_02
   │
   └─ Base_Curve
```

여기서 Bend는 원본 Mass_01이 아니라:

```text
Result(BooleanDifference)
```

를 입력받아야 한다.

따라서 Tree evaluation engine이 필요하다.

개념:

```python
def evaluate(node):
    if node is GeometryNode:
        return get_source_geometry(node.object_id)

    child_results = [evaluate(child) for child in node.children]

    if node is OperatorNode:
        return node.operator.evaluate(child_results)
```

실제 구현에서는:

- Cycle detection
- Cache
- Dirty propagation
- Failure handling

등을 추가해야 한다.

---

# 11. Modifier ON / OFF

C4D처럼 Modifier를 일시적으로 끌 수 있어야 한다.

예:

```text
✓ Boolean Difference
✓ Bend
□ Taper
```

Disabled Operator는 계산에서 bypass한다.

가능하면:

```text
Disabled Operator Result
=
Primary Input Result
```

방식으로 다음 단계에 전달한다.

단, Modifier별 Input 구조가 다른 경우 정확한 bypass rule을 정의해야 한다.

---

# 12. Drag & Drop

Tree에서 최소한 다음 Drag & Drop을 고려한다.

## 12.1 Input 순서 변경

Boolean A/B 변경 등.

## 12.2 Modifier 순서 변경

예:

```text
Taper
  └ Bend
```

를

```text
Bend
  └ Taper
```

로 변경하면 결과도 달라져야 한다.

## 12.3 Geometry를 Modifier 하위 Input으로 이동

Root:

```text
Mass_01
Mass_02
```

에서:

```text
▼ Boolean Difference
   ├─ Mass_01
   └─ Mass_02
```

로 구성할 수 있어야 한다.

---

# 13. Add Input 동작

장기적으로 Modifier Node에 `Add Input` 기능이 필요할 수 있다.

예:

```text
Right Click Boolean Difference
→ Add Input
→ Viewport에서 객체 선택
```

선택한 객체가 아직 Modifier Manager에 등록되지 않은 경우:

```text
자동 Register
→ 해당 Modifier Input으로 연결
```

하는 UX가 가장 단순하다.

사용자에게 먼저 `Add Object`를 수행하도록 강제하지 않는다.

---

# 14. Reference 개념

하나의 Rhino Object가 여러 Modifier에서 사용될 가능성이 있다.

같은 Geometry Node를 Tree 여러 곳에 실제로 복제하면 ownership과 transform 관계가 꼬일 수 있다.

장기적으로 별도 **Reference Node** 개념이 필요할 수 있다.

예:

```text
▼ Boolean Difference
   ├─ Mass_A
   └─ ↗ Shared_Core
```

실제 Geometry는 하나이며 동일한 Rhino GUID를 참조한다.

MVP에서는 Reference 기능을 생략하고 "한 Geometry Node는 하나의 Tree 위치에만 존재"하도록 제한해도 된다.

다만 데이터 모델은 향후 Reference 지원이 가능하도록 설계하는 것이 좋다.

---

# 15. Transform Hierarchy — 매우 중요한 요구사항

사용자가 Rhino Viewport에서 Modifier 결과 객체를 Move / Rotate / Scale할 때 **Tree에 연결된 객체 전체가 같이 움직여야 한다.**

예:

```text
▼ Bend
   ├─ ▼ Boolean Difference
   │    ├─ Mass_01
   │    └─ Mass_02
   └─ Base_Curve
```

최종 Bend 결과를 Rhino에서 Move하면:

```text
Mass_01
Mass_02
Base_Curve
```

가 모두 동일한 Transform을 받아야 한다.

즉 Tree의 상대 관계가 유지되어야 한다.

---

# 16. Root / Subtree / Geometry Transform 규칙

다음 세 경우를 구분한다.

## 16.1 최종 Root 결과를 Transform

예:

```text
Select Bend Result
Move +5000 X
```

기대 동작:

```text
Mass_01     +5000 X
Mass_02     +5000 X
Base_Curve  +5000 X
```

Tree 전체 Transform.

---

## 16.2 중간 Operator 결과를 Transform

예:

```text
▼ Taper
   └─ ▼ Bend
        ├─ ▼ Boolean
        │    ├─ Mass_01
        │    └─ Mass_02
        └─ Base_Curve
```

여기서 `Bend` Subtree만 이동했다면:

```text
Bend 이하 Child 전체
```

가 이동해야 한다.

상위 Taper는 이동된 Bend Result를 다시 계산한다.

원칙:

> Operator Node를 Transform하면 해당 Operator가 소유한 모든 Child가 동일한 Transform을 받는다.

---

## 16.3 내부 Geometry를 직접 Transform

예:

```text
Mass_02
```

만 Rhino에서 이동.

이 경우 Tree 전체가 움직이면 안 된다.

기대:

```text
Mass_02만 이동
    ↓
Boolean 재계산
    ↓
Bend 재계산
    ↓
최종 결과 갱신
```

즉 사용자는 Cutter 위치 등을 Rhino Viewport에서 직접 수정할 수 있어야 한다.

---

# 17. Local Transform / Tree Transform 분리 검토

가능하면 내부적으로 원본 좌표를 계속 덮어쓰기보다는 Transform 계층을 관리하는 방향을 검토한다.

개념:

```text
Final Transform
=
Parent / Tree Transform
×
Local Transform
```

예:

```text
Mass_02 Local Position = (5000, 3000, 0)

Tree Move = (+10000, 0, 0)
```

최종 표시 위치는 둘의 조합이다.

단, Rhino Document 객체 자체를 실제로 이동시키는 방식과 별도 Transform state를 들고 있는 방식 중 어느 것이 더 안정적인지는 프로토타입에서 검증해야 한다.

---

# 18. Reference Transform 규칙

향후 Reference Node를 지원할 경우 기본 규칙:

```text
Owned Child
→ Parent Transform 상속

Reference Child
→ Parent Transform 상속하지 않음
```

예:

```text
▼ Building_A
   └─ ▼ Boolean
        ├─ Mass_A
        └─ ↗ Shared_Core
```

Building_A를 이동해도 Shared_Core 원본 Rhino Object까지 이동하면 다른 Tree에 영향을 줄 수 있으므로 기본적으로 움직이지 않는 것이 안전하다.

이 기능은 MVP에서는 제외 가능.

---

# 19. Viewport와 Manager의 양방향 선택 동기화

최종적으로 다음 UX가 필요하다.

## Viewport → Manager

Rhino에서 등록된 객체를 선택하면 Manager에서 해당 Geometry / Tree가 highlight 또는 scroll.

## Manager → Viewport

Manager에서 Geometry Node 선택 시 Rhino 객체 Highlight.

Operator Node를 선택할 경우:

- Operator parameter panel 표시
- 가능하면 preview / control gizmo 표시

---

# 20. 결과 Geometry 표현 전략

이 부분은 Codex가 반드시 feasibility 검토할 것.

질문:

1. Operator 결과를 Rhino Document의 실제 객체로 둘 것인가?
2. DisplayConduit / Preview Geometry로만 표시할 것인가?
3. Root Result만 Document Object로 만들고 내부 Operator 결과는 cache로만 유지할 것인가?
4. 사용자가 Root Result를 Rhino 명령 / Gumball로 Transform하는 동작을 어떻게 감지할 것인가?
5. 원본 Geometry와 Result Geometry를 동시에 보이지 않게 어떻게 관리할 것인가?

초기 추천 검토안:

```text
Source Geometry
→ Rhino Document Object로 유지

Intermediate Results
→ Memory Cache / Preview

Final Root Result
→ DisplayConduit Preview 또는 관리되는 Result Object
```

하지만 Rhino Transform / selection UX와 충돌할 수 있으므로 가장 중요한 기술 검증 항목이다.

---

# 21. Source Geometry Visibility

Modifier Tree에 들어간 원본 Geometry가 최종 결과와 동시에 보이면 화면이 복잡해질 수 있다.

예:

```text
Mass_01
Mass_02
Final Boolean Result
```

세 개가 모두 표시되면 곤란하다.

필요한 개념:

- Source visibility
- Result visibility
- Edit mode
- Solo / Show Inputs
- Final Result only

예:

```text
Normal Mode
→ Final Result만 표시

Edit Mass_02
→ Mass_02 표시 + highlight
→ Final Result preview 유지
```

이 UX는 MVP 후반에 검토.

---

# 22. Undo / Redo

최종 제품에서 필수.

다음 작업이 Rhino Undo와 최대한 자연스럽게 연결되어야 한다.

- Add Object
- Remove Object
- Add Modifier
- Delete Modifier
- Modifier parameter 변경
- Drag & Drop
- Transform
- Rename

초기 feasibility prototype에서는 일부 생략할 수 있지만, 데이터 구조 설계 시 Undo 가능성을 고려해야 한다.

---

# 23. Persistence

Modifier Tree 정보는 `.3dm` 저장 후 다시 열어도 유지되어야 한다.

필요 데이터 예:

```text
Tree ID
Node ID
Node Type
Rhino Object GUID
Parent ID
Child Order
Modifier Type
Modifier Parameters
Enabled
Reference / Ownership
```

저장 방법 후보:

- RhinoDoc Strings
- Document UserData
- Object UserData
- External JSON sidecar

최종 제품에서는 `.3dm` 내부 저장을 우선 고려.

Codex는 RhinoCommon에서 안정적인 persistence 방법을 조사할 것.

---

# 24. Modifier Parameter 구조

각 Operator는 독립적인 parameter object를 가진다.

예:

```text
BooleanDifferenceParameters
- tolerance?
- manifold_only?
```

```text
BendParameters
- axis / baseline
- start
- end
- angle
- options
```

```text
TaperParameters
- axis
- start
- end
- factor / radii
```

중요:

Modifier Engine은 특정 Modifier 구현과 분리한다.

예:

```text
ModifierEngine
NodeModel
TreeEvaluator
TransformManager
DocumentSync
UI

Modifiers/
    BooleanDifference
    Bend
    Taper
```

새 Modifier를 추가할 때 Engine을 수정하지 않는 구조를 목표로 한다.

---

# 25. Bend 명칭 주의

현재 대화의 `Base_Curve를 따라 Bend`라는 표현은 Rhino의 전통적인 `Bend`와 `FlowAlongCurve`가 혼동될 수 있다.

프로토타입 전에 정확히 구분할 것.

예:

```text
Bend
- Target
- Bend Axis / Baseline
- Angle
```

```text
Flow Along Curve
- Target
- Base Curve
- Target Curve
```

초기 Tree 엔진 검증에는 어떤 Modifier든 사용할 수 있으므로, 가장 구현이 안정적인 SpaceMorph 계열부터 테스트해도 된다.

---

# 26. 구현 언어 / 구조 검토

현재 프로젝트 환경상 Rhino 8 + Python으로 prototype을 만들 수 있다.

하지만 다음 기능까지 갈 경우 C# RhinoCommon plugin이 장기적으로 더 적합할 가능성이 높다.

- Dockable Tree UI
- Drag & Drop
- Custom Icons
- Document Event tracking
- Gumball / Transform tracking
- Undo
- Persistent UserData
- 대량 객체 관리
- Viewport interaction
- Stable object lifecycle

따라서 Codex는 우선:

1. Python + Eto prototype 가능성
2. C# + RhinoCommon plugin 장기 구조

를 비교해도 된다.

단, MVP 자체는 빠른 feasibility 검증이 우선이다.

---

# 27. 가장 중요한 기술 리스크

## 27.1 Rhino Transform 감지

가장 큰 리스크.

사용자가 다음 방식으로 Tree Result를 움직였을 때:

- Move
- Rotate
- Scale
- Gumball
- Orient
- Mirror

플러그인이 이를 안정적으로 감지하고 **Tree 전체 또는 Subtree Transform으로 변환할 수 있는지** 검증 필요.

특히 Rhino가 결과 Geometry 자체를 Replace하는 이벤트와 Modifier Engine 재계산이 서로 루프를 만들 가능성 주의.

---

## 27.2 Source와 Result 객체의 identity

사용자가 Viewport에서 보고 클릭하는 "Result"가 실제 Rhino Object라면:

- GUID
- selection
- transform
- delete
- copy

등을 어떻게 처리할지 명확해야 한다.

---

## 27.3 Recursive Update Loop

예:

```text
User Move
→ Rhino Object Changed
→ Modifier Rebuild
→ Rhino Object Replace
→ Change Event
→ Modifier Rebuild
→ ...
```

이런 이벤트 루프 방지를 위한 transaction / suppression flag가 필요할 수 있다.

---

## 27.4 Boolean 안정성

Rhino Brep Boolean은 Geometry 변화에 따라 실패할 수 있다.

실패 시 전체 Tree가 사라지지 않도록:

```text
Boolean Difference   FAILED
Last Valid Result    유지
```

또는 상위 계산 중단 + 에러 표시 방식을 검토.

---

## 27.5 Circular Dependency

Tree Drag & Drop / Reference가 추가되면 cycle 가능.

예:

```text
A depends on B
B depends on A
```

연결 시점에 cycle detection 필수.

---

# 28. 실패 처리

Operator 실패 시 전체 모델을 삭제하거나 망가뜨리면 안 된다.

예:

```text
Boolean Difference   ✓
Bend                 ✕
Taper                skipped
```

가능한 처리:

- 실패 Node를 빨간색 표시
- Error message 저장
- Last valid result 표시
- 상위 Operator는 계산 중단
- Source Geometry는 유지

---

# 29. MVP 범위 제안

첫 Prototype에서는 **Boolean 계열만 구현**한다.

지원 Modifier:

```text
Boolean Union
Boolean Difference
Boolean Intersection
```

목표는 Boolean 기능 자체보다 **Procedural Tree Engine과 Rhino Transform 동기화 검증**이다.

## Phase 1 — Tree UI / Registration

목표:

- Dockable Modifier Manager
- Add Object
- Geometry Node 표시
- Rhino Object Name 동기화
- GUID 기반 Registry
- Tree Drag & Drop 기본
- Remove from Modifier

Geometry 연산 없이도 가능.

---

## Phase 2 — Boolean Operators

세 Operator를 구현한다.

```text
Boolean Union
Boolean Difference
Boolean Intersection
```

### Difference

```text
▼ Boolean Difference
   ├─ Mass_01
   └─ Mass_02
```

규칙:

- 첫 번째 Child = A
- 두 번째 이후 Child = B / Cutter

### Union

```text
▼ Boolean Union
   ├─ Mass_01
   ├─ Mass_02
   └─ Mass_03
```

규칙:

- 모든 Child를 동일한 Union Input으로 취급
- 위→아래 순서는 Tree 표시 순서로 유지하지만 연산 의미는 대칭적

### Intersection

```text
▼ Boolean Intersection
   ├─ Mass_01
   └─ Mass_02
```

규칙:

- 모든 Child의 공통 교집합을 계산
- 최소 2개 Input 필요

공통 요구사항:

- Child 순서 변경
- Source 변경 시 자동 재계산
- Result Preview
- Operator ON / OFF
- Boolean 실패 표시
- Last Valid Result 처리 검토

---

## Phase 3 — Nested Boolean Tree

Boolean 결과가 다른 Boolean의 Input이 될 수 있어야 한다.

예:

```text
▼ Boolean Difference
   ├─ ▼ Boolean Union
   │    ├─ Mass_01
   │    └─ Mass_02
   └─ Mass_03
```

해석:

```text
(Mass_01 ∪ Mass_02) - Mass_03
```

또는:

```text
▼ Boolean Intersection
   ├─ ▼ Boolean Difference
   │    ├─ Mass_01
   │    └─ Mass_02
   └─ Mass_03
```

해석:

```text
(Mass_01 - Mass_02) ∩ Mass_03
```

이 단계에서:

- Child → Parent recursive evaluation
- Dirty propagation
- Result caching
- Circular dependency 방지

를 검증한다.

---

## Phase 4 — Transform Prototype

가장 중요.

검증 시나리오:

### A
Root Result를 Move

기대:

```text
Tree 안의 Source Geometry 전체 동일 이동
```

### B
Mass_02만 Move

기대:

```text
Mass_02만 이동
→ Boolean 재계산
→ Bend 재계산
```

### C
중간 Operator Subtree Move

기대:

```text
해당 Operator 이하만 이동
→ 상위 Operator 재계산
```

이 Phase가 안정적으로 구현되지 않으면 프로젝트 전체 방향을 재검토한다.

---

# 30. MVP Acceptance Test

최소 성공 기준:

## Test 01

Rhino에서 Box 두 개 생성.

이름:

```text
Mass_01
Mass_02
```

`Add Object`로 등록.

Manager:

```text
Mass_01
Mass_02
```

표시.

---

## Test 02

Boolean Difference 생성.

```text
▼ Boolean Difference
   ├─ Mass_01
   └─ Mass_02
```

Viewport에서 결과 정상 표시.

---

## Test 03

Mass_02를 Rhino Viewport에서 Move.

Boolean 결과 자동 갱신.

---

## Test 04

Tree 순서 변경.

```text
Mass_01
Mass_02
```

↓

```text
Mass_02
Mass_01
```

Boolean 결과가 반대로 변경.

---

## Test 05

Boolean Union 추가.

```text
▼ Boolean Union
   ├─ Mass_01
   └─ Mass_02
```

두 Geometry가 정상적으로 Union되어야 한다.

---

## Test 06

Boolean Intersection 추가.

```text
▼ Boolean Intersection
   ├─ Mass_01
   └─ Mass_02
```

공통 영역만 결과로 생성되어야 한다.

---

## Test 07

Nested Boolean 구성.

```text
▼ Boolean Difference
   ├─ ▼ Boolean Union
   │    ├─ Mass_01
   │    └─ Mass_02
   └─ Mass_03
```

기대:

```text
(Mass_01 ∪ Mass_02) - Mass_03
```

---

## Test 08

Root Boolean Result 전체 Move.

기대:

Tree 내부의 Owned Geometry 전체가 동일한 Transform을 받고
Boolean 결과가 같은 형상을 유지한 채 이동.

---

## Test 09

내부 Geometry 하나만 Move.

예:

```text
Mass_02
```

기대:

```text
Mass_02 수정
→ 관련 Boolean 재계산
→ 상위 Boolean 재계산
```

Root Tree의 전체 배치 Transform은 유지.

---

# 31. 이번 Prototype에서 하지 않아도 되는 것

초기 검증을 위해 다음은 과감히 제외 가능.

- 모든 Rhino Modifier 지원
- Grasshopper 수준의 자유 연결
- 완성형 Object Manager
- Blocks
- Instances
- External References
- Animation
- Keyframe
- Material
- Render visibility
- 완성형 Undo
- 모든 Rhino Command 추적
- 여러 Document 동시 지원
- 복잡한 Folder / Null Object
- 모든 Geometry Type 지원
- 최종 패키징 / 배포

---

# 32. Codex에게 우선 요청할 작업

1. 이 설계가 RhinoCommon 구조상 가능한지 기술 검토.
2. 특히 **Root Result / Subtree / Geometry Transform을 Rhino Viewport 작업과 연결하는 방법**을 우선 조사.
3. Rhino Object event / Replace / Transform / selection / Gumball 처리 방법 조사.
4. Source Geometry와 Generated Result를 어떻게 분리할지 제안.
5. Recursive evaluation engine의 가장 단순한 데이터 모델 설계.
6. Add Object + Tree UI prototype 구현.
7. Boolean Union / Difference / Intersection 3개 Operator 구현.
8. Nested Boolean Tree 구현.
9. Transform Prototype까지 구현 후 결과를 보고 Bend / Taper 등 변형 Modifier 추가 여부 결정.

---

# 33. 프로젝트의 핵심 원칙 요약

```text
1. Rhino의 모든 객체를 관리하지 않는다.
   사용자가 Add Object로 등록한 객체만 Modifier 시스템에 참여한다.

2. Geometry 이름은 Rhino Object Name을 따른다.
   내부 식별은 GUID를 사용한다.

3. Parent는 Operator, Child는 Input이다.

4. 계산은 Child → Parent 방향이다.

5. Modifier의 결과는 다른 Modifier의 Input이 될 수 있다.

6. Boolean은 첫 번째 Child를 A,
   두 번째 이후 Child를 B/Cutter로 해석한다.

7. Child 순서는 연산 의미를 가진다.

8. Tree 구조는 Drag & Drop으로 수정 가능해야 한다.

9. 최종 Result를 Transform하면
   Tree 내부의 Owned Child 전체가 같이 Transform되어야 한다.

10. 내부 Geometry만 Transform하면
    해당 Geometry만 변경하고 상위 Operator를 다시 계산한다.

11. Rhino의 일반 모델링 UX를 최대한 유지한다.

12. Grasshopper를 대체하지 않는다.
    가벼운 Object-based procedural modeling layer를 목표로 한다.
```

---

# 34. 최종 목표 이미지

사용자는 Rhino에서 평소처럼 Geometry를 만든다.

```text
Mass_01
Mass_02
Base_Curve
```

필요한 객체만 Modifier Manager에 등록한다.

```text
[ + Add Object ]
```

초기 단계에서는 Boolean Tree만 구성한다.

예:

```text
▼ Boolean Difference
   ├─ ▼ Boolean Union
   │    ├─ Mass_01
   │    └─ Mass_02
   └─ Mass_03
```

이후:

- Mass_02 이동 → Union 재계산 → Difference 재계산
- Boolean Child 순서 변경 → Difference의 A/B 의미 변경
- Union / Difference / Intersection ON/OFF
- Nested Boolean 결과 자동 갱신
- Final Result 이동 → Tree 전체 이동

이 정도가 자연스럽게 동작한다면 이 프로젝트는 충분히 다음 단계로 발전시킬 가치가 있다.

가장 먼저 검증할 것은 **"Rhino Viewport에서의 Transform과 Procedural Tree를 안정적으로 동기화할 수 있는가"**이다.


---

# 35. 초기 구현 범위 확정 — Boolean Only

초기 Prototype에서는 아래 세 기능만 구현한다.

```text
Boolean Union
Boolean Difference
Boolean Intersection
```

다음 Modifier는 구현하지 않는다.

```text
Bend
Taper
Twist
Extrude
Flow
Array
Mirror
Fillet
```

이 결정의 이유:

1. Geometry deformation보다 Tree / dependency / transform 시스템이 핵심 리스크다.
2. Boolean 세 종류만으로도 Nested Operator 구조를 충분히 검증할 수 있다.
3. Boolean은 건축 Mass 작업에서 활용 빈도가 높다.
4. Source Geometry 이동 → Result 자동 갱신 흐름을 검증하기 좋다.
5. Transform Hierarchy가 안정적으로 동작하는지 먼저 확인해야 한다.
6. 초기 엔진이 검증된 뒤 Bend / Taper 등의 Modifier를 추가하는 편이 안전하다.

초기 성공 기준은 "Boolean 기능이 된다"가 아니라 다음이다.

```text
Registered Rhino Geometry
        ↓
Boolean Operator Tree
        ↓
Nested Evaluation
        ↓
Source Edit / Move
        ↓
Automatic Rebuild
        ↓
Root / Subtree Transform 유지
```

이 흐름이 Rhino 8에서 안정적으로 작동하면 다음 단계에서 Deformation Modifier를 추가한다.
