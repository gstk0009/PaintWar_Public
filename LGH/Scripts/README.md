# LGH 스크립트 구조

폴더는 기능과 책임을 기준으로 구분합니다. 스크립트를 이동할 때는 Unity의 `AssetDatabase.MoveAsset`을 사용해 `.meta` GUID와 씬·프리팹 참조를 보존합니다.

| 폴더 | 역할 |
| --- | --- |
| `Audio` | 사운드 관리. 발소리 테스트는 `Testing`에 둡니다. |
| `Bot` | `Controllers`, `Decision`, `Movement`로 봇 실행·판단·이동을 구분합니다. |
| `Camera` | 게임플레이 카메라 추적. |
| `Lobby/Skills` | 로비 스킬 선택과 로드아웃 저장. |
| `Networking/Matchmaking` | 매칭 보조 데이터와 매치 종료 처리. |
| `Networking/Rooms` | 방 할당·저장·프로세스 실행·서버 수명 관리. |
| `Obstacles` | `Generation`, `Runtime`, `Visuals`로 맵 생성·장애물 동작·연출을 구분합니다. |
| `Players` | `Core`, `Movement`, `Input`, `Networking`, `Claiming`, `Identity`, `Progression`, `Testing`으로 플레이어 책임을 구분합니다. |
| `Round` | `Core`, `Networking`, `Participants`, `UI`, `Zones`로 라운드 책임을 구분합니다. |
| `StatusEffects` | 효과 정의는 `Data`, 실행 처리는 `Runtime`, 네트워크 동기화는 `Networking`에 둡니다. |
| `Tiles` | `Generation`, `Grid`, `Claiming`, `Networking`, `Rendering`으로 타일 생성·상태·점령·권한·표시를 구분합니다. |
| `UI` | `HUD`, `Layout`, `Guide`, `Testing`으로 게임 UI를 구분합니다. |
| `Utilities` | 여러 기능에서 공유하는 유틸리티. |
| `Weapons` | `Data`, `Patterns`, `Runtime`, `Testing`, `Visuals`로 무기 책임을 구분합니다. |

`Testing`의 MonoBehaviour는 씬에서 실행하는 테스트 도구이며, Editor 전용 코드는 `Editor` 폴더에 둡니다. 스크립트의 위치를 바꾸기 위해 연결된 씬·프리팹·설정 에셋까지 함께 이동할 필요는 없습니다.
