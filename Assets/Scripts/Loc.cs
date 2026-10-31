using System;
using System.Collections.Generic;
using UnityEngine;

public enum Lang
{
    En,
    Ko,
}

// All player-facing strings in English and Korean. Rich text tags are kept inside the strings.
public static class Loc
{
    const string SaveKey = "lang";

    public static event Action Changed;

    static Lang lang;
    static bool loaded;

    public static Lang Current
    {
        get
        {
            if (!loaded)
            {
                lang = (Lang)PlayerPrefs.GetInt(SaveKey, (int)Lang.Ko);
                loaded = true;
            }
            return lang;
        }
        set
        {
            if (Current == value) return;
            lang = value;
            PlayerPrefs.SetInt(SaveKey, (int)value);
            PlayerPrefs.Save(); // WebGL writes PlayerPrefs to IndexedDB only on Save
            Changed?.Invoke();
        }
    }

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var pair)) return key;
        return Current == Lang.Ko ? pair.ko : pair.en;
    }

    public static string T(string key, params object[] args) => string.Format(T(key), args);

    static readonly Dictionary<string, (string en, string ko)> Table = new Dictionary<string, (string, string)>
    {
        // Menu
        ["menu.title"] = ("TRICK OR TREAT", "TRICK OR TREAT"),
        ["menu.subtitle"] = ("Hand candy to the Kunmungchi. Send the disguised Omangi away.", "쿤뭉치에게는 사탕을, 변장한 오망이는 쫓아내세요."),
        ["menu.best"] = ("Best: {0}", "최고 점수: {0}"),
        ["menu.start"] = ("START", "시작"),
        ["menu.gallery"] = ("GALLERY  {0}/{1}", "도감  {0}/{1}"),
        ["menu.dressup"] = ("DRESS UP", "옷 입히기"),
        ["menu.language"] = ("LANGUAGE: EN", "언어: 한국어"),
        ["menu.give"] = ("Give candy", "사탕 주기"),
        ["menu.away"] = ("Send away", "쫓아내기"),
        ["menu.blast"] = ("Palm blast", "손바닥 날리기"),
        ["menu.blastCaption"] = ("Space / wheel: blows off sheets and props.", "스페이스 / 휠: 시트와 소품을 날려요."),

        // Settings
        ["menu.settings"] = ("SETTINGS", "설정"),
        ["settings.header"] = ("SETTINGS", "설정"),
        ["settings.tabGeneral"] = ("GENERAL", "일반"),
        ["settings.tabLook"] = ("ADVANCED LOOK", "고급 화면"),
        ["settings.preset"] = ("Preset", "화면 모드"),
        ["settings.presetOriginal"] = ("Original", "원본"),
        ["settings.presetRough"] = ("Rough", "거칠게"),
        ["settings.presetGentle"] = ("Gentle", "은은하게"),
        ["settings.presetSoft"] = ("Soft", "부드럽게"),
        ["settings.presetSharp"] = ("Sharp", "선명하게"),
        ["settings.presetHint"] = ("Presets set the whole look at once. Fine-tune it in the Advanced Look tab.", "프리셋은 화면 옵션을 한 번에 맞춰요. 세부 조정은 고급 화면 탭에서 해요."),
        ["settings.presetCustom"] = ("Custom look (fine-tuned in the Advanced Look tab).", "사용자 설정 상태예요 (고급 화면 탭에서 조정됨)."),
        ["settings.audio"] = ("Sound", "소리"),
        ["settings.master"] = ("Master", "전체 음량"),
        ["settings.sfx"] = ("Effects", "효과음"),
        ["settings.music"] = ("Music", "음악"),
        ["settings.graphics"] = ("Look", "화면"),
        ["settings.dither"] = ("Dither", "디더링"),
        ["settings.on"] = ("ON", "켜짐"),
        ["settings.off"] = ("OFF", "꺼짐"),
        ["settings.flat"] = ("Flat areas clean", "평평한 면 유지"),
        ["settings.pattern"] = ("Pattern", "무늬"),
        ["settings.patNoise"] = ("Noise", "블루노이즈"),
        ["settings.patFine"] = ("Fine", "촘촘함"),
        ["settings.patMedium"] = ("Medium", "보통"),
        ["settings.patCoarse"] = ("Coarse", "굵게"),
        ["settings.levels"] = ("Colors", "색 단계"),
        ["settings.dark"] = ("Shadow colors", "어두운 곳 색 단계"),
        ["settings.resolution"] = ("Resolution", "해상도"),
        ["settings.resolutionHint"] = ("Resolution is the share of screen pixels drawn. Higher is sharper but slower.", "해상도는 화면 픽셀 중 그려지는 비율이에요. 높을수록 선명하지만 느려요."),
        ["settings.language"] = ("Language", "언어"),
        ["settings.close"] = ("CLOSE", "닫기"),

        // Records, score and initials
        ["menu.records"] = ("RECORDS", "기록"),
        ["records.header"] = ("RECORDS", "기록"),
        ["ranking.header"] = ("RANKING", "랭킹"),
        ["records.tabMine"] = ("My records", "내 기록"),
        ["records.tabStats"] = ("My stats", "내 통계"),
        ["records.empty"] = ("No records yet.\nPlay a game to set one.", "아직 기록이 없어요.\n게임을 하고 기록을 남겨 보세요."),
        ["ranking.tabLocal"] = ("LOCAL", "로컬"),
        ["ranking.tabOnline"] = ("ONLINE", "온라인"),
        ["ranking.clear"] = ("CLEAR RANKING", "랭킹 지우기"),
        ["ranking.clearAsk"] = ("The local ranking and its recorded looks\nwill be deleted. This cannot be undone.", "이 기기의 로컬 랭킹과 기록된 룩이\n삭제됩니다. 되돌릴 수 없어요."),
        ["ranking.retry"] = ("RETRY", "다시 시도"),
        ["online.notConfigured"] = ("Online ranking is offline.", "서버가 설정되지 않았어요."),
        ["online.loading"] = ("Loading...", "불러오는 중..."),
        ["online.failed"] = ("Could not load the ranking.", "랭킹을 불러오지 못했어요."),
        ["online.empty"] = ("No records yet.", "아직 기록이 없어요."),
        ["settings.resetAll"] = ("Reset all progress", "모든 진행상황 초기화"),
        ["settings.resetAsk"] = ("Your best score, stats and gallery will be deleted.\nRankings are kept. This cannot be undone.", "최고 점수, 통계, 도감이 삭제됩니다.\n랭킹은 유지돼요. 되돌릴 수 없어요."),
        ["settings.resetYes"] = ("DELETE", "삭제"),
        ["settings.resetNo"] = ("CANCEL", "취소"),
        ["settings.resetDone"] = ("Reset done.", "초기화했어요."),
        ["settings.share"] = ("Share results online", "결과 온라인 공유"),
        ["settings.shareHint"] = ("Only an anonymous result code is sent. Initials are sent only when you RECORD.", "익명 결과 코드만 전송돼요. 이니셜은 기록할 때만 보내요."),
        ["records.page"] = ("{0} / {1}", "{0} / {1}"),
        ["stats.games"] = ("Games played: {0}", "플레이 횟수: {0}판"),
        ["stats.total"] = ("Total score: {0}", "총점: {0}"),
        ["stats.average"] = ("Average score: {0}", "평균 점수: {0}"),
        ["stats.gave"] = ("Gave candy to an Omangi: {0}", "오망이에게 사탕을 줌: {0}"),
        ["stats.sent"] = ("Sent a Kunmungchi away: {0}", "쿤뭉치를 쫓아냄: {0}"),
        ["stats.fooled"] = ("Looks that fooled you most", "나를 가장 많이 속인 변장"),
        ["stats.times"] = ("{0} times", "{0}번"),
        ["stats.none"] = ("Nothing yet.", "아직 없어요."),
        ["stats.reset"] = ("RESET STATS", "통계 초기화"),
        ["stats.resetConfirm"] = ("REALLY RESET? PRESS AGAIN", "정말 지울까요? 한 번 더 누르세요"),
        ["score.newBest"] = ("NEW BEST!", "최고 기록!"),
        ["score.best"] = ("Best {0}", "최고 {0}"),
        ["score.rank"] = ("Local rank #{0}", "내 기록 {0}위"),
        ["score.notRanked"] = ("Not in your top 20 this time.", "이번엔 내 기록 20위 안에 들지 못했어요."),
        ["score.question"] = ("Record this score?", "이 점수를 기록할까요?"),
        ["score.record"] = ("RECORD", "기록하기"),
        ["score.skip"] = ("SKIP", "건너뛰기"),
        ["score.next"] = ("NEXT", "다음"),
        ["initials.header"] = ("NEW RECORD #{0}", "새 기록 #{0}"),
        ["initials.hint"] = ("Pick 3 letters. Type A-Z or use the arrows.", "글자 3개를 골라요. A-Z를 입력하거나 화살표를 써요."),
        ["initials.ok"] = ("OK", "확인"),
        ["review.tabLooks"] = ("This game", "이번 판"),
        ["review.tabRanking"] = ("Ranking", "랭킹"),
        ["review.localRank"] = ("Local #{0}", "내 기록 {0}위"),

        // Touch and phone
        ["touch.hint"] = ("← Send away     Tap / ↑↓: palm blast     Give candy →", "← 쫓아내기     탭 / ↑↓ : 손바닥 날리기     사탕 주기 →"),
        ["rotate.title"] = ("ROTATE YOUR PHONE", "폰을 돌려 주세요"),
        ["rotate.hint"] = ("This game is played in landscape.", "이 게임은 가로 화면으로 해요."),

        // Common
        ["back"] = ("BACK", "뒤로"),
        ["drag.rotate"] = ("Drag to rotate · Wheel to zoom", "드래그: 회전 · 휠: 확대"),
        ["drag.rotateTouch"] = ("Drag to rotate · Pinch to zoom", "드래그: 회전 · 핀치: 확대"),
        ["menu.carouselHint"] = ("A / D, arrows, wheel or drag: choose    Enter or click: confirm", "A / D, 방향키, 휠, 드래그: 고르기    Enter, 클릭: 확인"),
        ["menu.carouselHintTouch"] = ("Swipe sideways to choose    Tap to confirm", "옆으로 밀어서 고르기    탭해서 확인"),
        ["intro.skip"] = ("Click to skip", "클릭하면 건너뛰기"),
        ["intro.skipTouch"] = ("Tap to skip", "탭하면 건너뛰기"),
        ["gallery.special"] = ("SPECIAL", "특별"),
        ["gallery.unlockAll"] = ("UNLOCK ALL", "전부 해금"),
        ["gallery.unlockAsk"] = ("Unlock everything?\nThis cannot be undone.", "모든 항목을 해금할까요?\n되돌릴 수 없어요."),
        ["gallery.unlockYes"] = ("UNLOCK", "해금"),
        ["gallery.unlockNo"] = ("CANCEL", "취소"),
        ["view.reset"] = ("VIEW RESET", "시점 초기화"),

        // Gallery
        ["gallery.header"] = ("GALLERY", "도감"),
        ["gallery.found"] = ("Found {0} of {1}", "발견 {0} / {1}"),
        ["gallery.empty"] = ("Nothing found yet.\nPlay the game to meet them.", "아직 만난 모습이 없어요.\n게임을 하며 만나보세요."),

        // Dress up
        ["dress.header"] = ("DRESS UP", "옷 입히기"),
        ["dress.who"] = ("Who", "누구"),
        ["dress.fur"] = ("Fur", "털"),
        ["dress.furdye"] = ("Fur dye", "털 염색"),
        ["dress.props"] = ("Props", "소품"),
        ["dress.costumes"] = ("Costumes", "코스튬"),
        ["dress.sheet"] = ("Ghost sheet", "유령 시트"),
        ["dress.random"] = ("RANDOM", "무작위"),
        ["dress.reset"] = ("RESET", "초기화"),

        // Review
        ["review.header"] = ("GAME OVER", "게임 오버"),
        ["review.score"] = ("Score {0}      Best {1}", "점수 {0}      최고 {1}"),
        ["review.hint"] = ("The looks that cost you a heart. Drag a model to turn it.", "하트를 잃게 만든 모습이에요. 드래그해서 돌려보세요."),
        ["review.playagain"] = ("PLAY AGAIN", "다시 하기"),
        ["review.title"] = ("TITLE", "타이틀"),
        ["review.gave"] = ("You gave it candy", "사탕을 줬어요"),
        ["review.sent"] = ("You sent it away", "쫓아냈어요"),
        ["review.wasKun"] = ("It was a Kunmungchi!", "쿤뭉치였어요!"),
        ["review.wasOma"] = ("It was an Omangi!", "오망이였어요!"),
        ["review.look"] = ("Look #{0}", "모습 #{0}"),
        ["review.sheet"] = (" (under a ghost sheet)", " (유령 시트 안)"),

        // HUD
        ["hud.correct"] = ("Correct!", "정답!"),
        ["hud.wrongOma"] = ("That was an Omangi!", "오망이였어요!"),
        ["hud.wrongKun"] = ("That was a Kunmungchi!", "쿤뭉치였어요!"),
        ["hud.newLook"] = ("New look: {0} #{1}", "새 모습: {0} #{1}"),

        // Catalog
        ["kind.kun"] = ("Kunmungchi", "쿤뭉치"),
        ["kind.oma"] = ("Omangi", "오망이"),
        ["outfit.sheet"] = ("Ghost sheet", "유령 시트"),
        ["hud.special"] = ("SPECIAL GUEST: {0}", "SPECIAL GUEST: {0}"),
        ["hud.specialNew"] = ("NEW SPECIAL GUEST: {0}", "NEW SPECIAL GUEST: {0}"),
        ["outfit.dye"] = ("Fur dye {0}", "털 염색 {0}"),
        ["outfit.none"] = ("No disguise", "변장 없음"),

        // Props, keyed by the model node name without the Fake_ prefix
        ["prop.ConeNose"] = ("Cone nose", "원뿔 코"),
        ["prop.GoatEars"] = ("Goat ears", "염소 귀"),
        ["prop.HornBand"] = ("Horn band", "뿔 머리띠"),
        ["prop.PaintBucket"] = ("Paint bucket", "페인트 통"),
        ["prop.RaccoonTail"] = ("Raccoon tail", "너구리 꼬리"),
        ["prop.XGlasses"] = ("X glasses", "X자 안경"),

        // Costumes, keyed by the model node name without the Costume_ prefix
        ["costume.WitchHat"] = ("Witch hat", "마녀 모자"),
        ["costume.PumpkinBasket"] = ("Pumpkin basket", "호박 바구니"),
        ["costume.Bandage"] = ("Bandages", "붕대"),
        ["costume.BatWings"] = ("Bat wings", "박쥐 날개"),
        ["costume.BowTie"] = ("Bow tie", "나비넥타이"),
        ["costume.MaidHeadband"] = ("Maid headband", "메이드 머리띠"),
        ["costume.BunnyHeadband"] = ("Bunny ears", "토끼귀 머리띠"),
        ["costume.StarSunglasses"] = ("Star sunglasses", "별 선글라스"),
        ["costume.AlienSunglasses"] = ("Alien sunglasses", "외계인 선글라스"),
    };

    public static bool Has(string key) => Table.ContainsKey(key);
}
