// Highlights of each build, newest first, one short line per change (English, Korean), written for players, not developers.
// date is the release time, 24-hour clock: set it when the build is made.
// The detailed notes are kept in Docs/PatchNotes.md. Add the new build here first: its version is shown on the title screen.
public static class PatchNotes
{
    public struct Entry
    {
        public string version;
        public string date;
        public (string en, string ko)[] lines;
    }

    public static readonly Entry[] Entries =
    {
        new Entry
        {
            version = "0.3.0", date = "2026-11-01 23:22",
            lines = new[]
            {
                ("Combo counter and time limit, with a bar at the bottom", "콤보 표시와 제한 시간 게이지가 생겼어요"),
                ("Your hand gets faster as your combo grows", "콤보가 쌓일수록 손이 빨라져요"),
                ("A long combo makes the night spookier", "콤보가 길수록 밤이 으스스해져요"),
                ("Tricky disguises now come much later", "어려운 위장은 훨씬 나중에 나와요"),
                ("More varied visitors, some with no disguise at all", "더 다양한 손님, 아무것도 안 걸친 손님도 가끔 와요"),
                ("New costumes: pirate hat, hook, bunny suit and more", "새 분장: 해적 모자, 갈고리, 토끼 슈트 등"),
                ("Palm blast removes disguise props first; fallen visitors get up faster", "장풍은 위장 도구부터 날리고, 쓰러진 손님은 빨리 일어나요"),
                ("Dress-up: choose by body part", "옷입히기: 부위별로 고르기"),
                ("See what changed in each version: the i button on the title screen", "타이틀의 i 버튼에서 버전별 변경점 확인"),
                ("World ranking sharing is set in the settings only; Records models no longer cut off", "월드랭킹 공유는 설정에서만, 기록 화면 잘림 수정"),
            },
        },
        new Entry
        {
            version = "0.2.0", date = "2026-11-01 09:48",
            lines = new[]
            {
                ("Online ranking, world statistics, and the looks that fooled other players", "온라인 랭킹, 월드 통계, 다른 사람이 속은 모습 보기"),
                ("Special guests Moku and Myeong7 show up", "특별 손님 Moku, Myeong7 등장"),
                ("Give-up button: your score so far still counts", "포기 버튼: 지금까지 점수는 인정돼요"),
                ("Kunmungchi introduction and tips in your first game", "쿤뭉치 소개 화면과 첫 게임 안내"),
                ("Swipe through the title menu", "타이틀 메뉴를 좌우로 넘겨서 고르기"),
                ("Gallery: special tab, unlock all, zoom and move", "도감: 특별 손님 탭, 모두 해금, 확대와 이동"),
                ("Settings: reset progress, skip the intro", "설정: 진행 초기화, 인트로 건너뛰기"),
            },
        },
        new Entry
        {
            version = "0.1.0", date = "2026-10-31 23:50",
            lines = new[]
            {
                ("First release", "첫 배포"),
            },
        },
    };

    public static string Current => Entries[0].version;
}
