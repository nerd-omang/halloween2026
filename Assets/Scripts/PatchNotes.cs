// Highlights of each build, newest first, one short line per change (English, Korean).
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
            version = "0.3", date = "2026-11-01",
            lines = new[]
            {
                ("The current combo is shown under the score", "점수 아래에 현재 콤보 표시"),
                ("Combos need a quick answer: a gauge at the bottom shows the time left", "콤보는 제한 시간 안에 답해야 유지, 하단 게이지로 표시"),
                ("Update history on the title screen", "타이틀 화면에 업데이트 내역 추가"),
                ("World ranking sharing is set in the settings only", "월드랭킹 공유는 설정에서만 변경"),
                ("Fixed the model row being cut off on the Records tab", "기록 탭에서 모델이 잘리던 문제 수정"),
            },
        },
        new Entry
        {
            version = "0.2", date = "2026-11-01",
            lines = new[]
            {
                ("Online ranking, world statistics and others' mistaken looks", "온라인 랭킹, 월드 통계, 다른 사람이 틀린 외형 보기"),
                ("Special guests Moku and Myeong7", "특별 손님 Moku, Myeong7 등장"),
                ("Give up button: your score so far counts", "게임 포기 버튼: 지금까지의 점수 인정"),
                ("Kunmungchi intro and first-game hints", "쿤뭉치 소개 연출과 첫 게임 조작 안내"),
                ("Title menu is now a swipeable carousel", "타이틀 메뉴를 좌우로 넘기는 방식으로 변경"),
                ("Gallery: special tab, unlock all, zoom and move", "도감: 특별 탭, 모두 해금, 확대와 이동"),
                ("Settings: reset progress, skip intro", "설정: 진행상황 초기화, 인트로 건너뛰기"),
            },
        },
        new Entry
        {
            version = "0.1", date = "2026-10-31",
            lines = new[]
            {
                ("First release", "첫 배포"),
            },
        },
    };

    public static string Current => Entries[0].version;
}
