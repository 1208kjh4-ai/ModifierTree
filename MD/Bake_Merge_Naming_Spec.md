# Bake / Merge / Modifier 이름 표시

이 문서는 사용자가 정한 동작을 기록합니다. 이름, Bake와 Merge는 0.10.0에 구현했고 기본 동작은 사용자 확인을 받았습니다. 0.11.0에서는 Union/Intersection을 추가하여 아래 예시도 사용할 수 있습니다.

## 이름 표시

Modifier의 종류와 사용자 이름을 분리합니다. Boolean Difference에 `Main`을 지정하면 트리에는 `(BD) Main`을 표시합니다. Boolean Union은 `BU`, Boolean Intersection은 `BI`, Bend처럼 짧은 종류명은 `Bend`를 괄호 안에 그대로 쓸 수 있게 구성합니다.

## 기준 트리

```text
Boolean Union
  (BD) Main
    Brep A
    Brep B
  (BD) Sub
    Brep C
    Brep D
```

## Main을 Bake

현재 Main의 계산 결과를 독립적인 Rhino 객체로 만들고, 트리 최상위에 `Main`으로 등록합니다. 기존 Main Modifier와 하위 구조는 유지합니다.

```text
Main
Boolean Union
  (BD) Main
    Brep A
    Brep B
  (BD) Sub
    Brep C
    Brep D
```

## Main을 Merge

현재 Main의 계산 결과를 고정된 Rhino 객체로 만들고, 기존 Main Modifier와 그 하위 트리 대신 `Main`을 넣습니다. 원래 부모와 형제 순서를 유지하며, 상위 Modifier는 이 고정 결과를 입력으로 사용합니다.

```text
Boolean Union
  Main
  (BD) Sub
    Brep C
    Brep D
```

결과가 서로 떨어진 여러 조각이어도 하나의 Rhino Brep 객체와 트리 행으로 다룹니다. 조각 사이를 채우거나 추가 Union을 적용하지 않습니다. 기존 하위 원본은 문서에서 숨겨 보관하며, Undo 시 원래 트리와 표시 상태를 함께 복원합니다. 생성된 결과와 이름은 일반 Rhino 객체이므로 이후 원본의 변화에 따라 갱신되지 않습니다.
