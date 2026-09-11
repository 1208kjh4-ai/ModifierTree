# 뷰포트 표시 방식 검토 — 0.4.0

요구사항은 미리보기가 각 뷰포트의 현재 표시 방식을 따르는 것입니다. 활성 뷰의 설정을 모든 뷰에 복사하면 Wireframe과 Shaded를 동시에 사용하는 경우 잘못 표시되므로, 그리는 뷰포트의 설정을 매번 읽습니다.

## 이번 구현

`DifferenceConduit`는 그리는 뷰의 `DisplayMode.DisplayAttributes`에서 면 표시와 가장자리·등매개선 표시를 읽습니다.
고정 청록색은 제거하고 첫 번째 입력을 따라 내려가 찾은 원본의 객체/레이어 색상을 사용합니다. 모드가 지정 재질을 사용하면 첫 원본의 기본 재질을 가져옵니다. 사용자 색상과 투명도 설정도 반영합니다.
실패한 캐시만 주황색을 유지합니다. 입력 선은 객체별 명시적 표시 설정으로 제어하며 기본은 숨김입니다.

공식 API의 [DisplayModeDescription](https://developer.rhino3d.com/api/RhinoCommon/html/T_Rhino_Display_DisplayModeDescription.htm)은 표시 모드와 속성 접근을 제공합니다.
[DrawBrepShaded](https://developer.rhino3d.com/api/rhinocommon/rhino.display.displaypipeline/drawbrepshaded)는 주어진 재질로 Brep의 메시 표현을 그리고, [Display Conduits 가이드](https://developer.rhino3d.com/en/guides/rhinocommon/display-conduits/)는 별도 표시 단계에서 도형을 그리는 방식을 설명합니다.

## 동일성의 한계와 후속 판단

현재 구현은 표시 속성을 읽어 직접 그리는 방식입니다. 일반 Rhino 객체가 표시·렌더 파이프라인 전체에 참여하는 것과 같다고 간주할 수 없습니다.
따라서 Wireframe/Shaded 등의 기본 면·선 표시, 기본 색상과 투명도를 따르는 수준으로 정의합니다. Technical의 고급 선 규칙, 텍스처 매핑, Raytraced, 그림자·반사·렌더러별 효과까지 동일하다고 보장하지 않습니다.
이것은 현재 코드 구조와 API 역할에 근거한 구현 판단입니다. 사용자 화면에서 각 모드를 비교해야 합니다.

완전한 표시 일치가 필수라면 실제 결과 객체 또는 렌더러 연동 방식을 별도로 검토해야 합니다. 그 단계에는 결과 GUID, 원본 표시, 선택·삭제·복사 정책과 Undo/Redo를 함께 설계해야 합니다.
현재는 재계산 시 문서 객체를 추가·교체하지 않아 기존 원본 Undo/Redo 구조를 유지합니다.

## 설치된 API에서 확인한 주의점

Rhino 8.34의 `FrontMaterialTransparency`는 실제로 0~1 범위입니다. 설치 XML은 0~100이라고 설명하지만, 네이티브 검사에서 65를 지정하면 1로 제한되고 0.65를 지정하면 그대로 읽히는 것을 확인했습니다.
또한 `UseCustomObjectColor`와 `FrontOverrideObjectColor`는 연동되므로 두 값을 독립 조건으로 처리하여 지정 색상을 덮어쓰지 않도록 했습니다.
이 규칙들은 자동 검사로 확인했으며 실제 GPU 표시를 검사한 것은 아닙니다.
