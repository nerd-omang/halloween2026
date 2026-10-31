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

    static Lang lang = (Lang)PlayerPrefs.GetInt(SaveKey, (int)Lang.Ko);

    public static Lang Current
    {
        get => lang;
        set
        {
            if (lang == value) return;
            lang = value;
            PlayerPrefs.SetInt(SaveKey, (int)value);
            Changed?.Invoke();
        }
    }

    public static string T(string key)
    {
        if (!Table.TryGetValue(key, out var pair)) return key;
        return lang == Lang.Ko ? pair.ko : pair.en;
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
        ["menu.blastKeys"] = ("Space / wheel click / scroll", "스페이스 / 휠클릭 / 휠"),
        ["menu.blastHint"] = ("Blows off a sheet and knocks off a prop.", "시트를 날리고 소품을 떨어뜨려요."),

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
        ["settings.patNoise"] = ("Blue noise", "블루노이즈"),
        ["settings.patFine"] = ("Fine", "촘촘함"),
        ["settings.patMedium"] = ("Medium", "보통"),
        ["settings.patCoarse"] = ("Coarse", "굵게"),
        ["settings.levels"] = ("Colors", "색 단계"),
        ["settings.dark"] = ("Shadow colors", "어두운 곳 색 단계"),
        ["settings.resolution"] = ("Resolution", "해상도"),
        ["settings.resolutionHint"] = ("Resolution is the share of screen pixels drawn. Higher is sharper but slower.", "해상도는 화면 픽셀 중 그려지는 비율이에요. 높을수록 선명하지만 느려요."),
        ["settings.language"] = ("Language", "언어"),
        ["settings.close"] = ("CLOSE", "닫기"),

        // Touch and phone
        ["touch.hint"] = ("← Send away     Tap / ↑↓: palm blast     Give candy →", "← 쫓아내기     탭 / ↑↓ : 손바닥 날리기     사탕 주기 →"),
        ["rotate.title"] = ("ROTATE YOUR PHONE", "폰을 돌려 주세요"),
        ["rotate.hint"] = ("This game is played in landscape.", "이 게임은 가로 화면으로 해요."),

        // Common
        ["back"] = ("BACK", "뒤로"),
        ["drag.rotate"] = ("Drag to rotate", "드래그해서 돌려보기"),

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
    };

    public static bool Has(string key) => Table.ContainsKey(key);
}
