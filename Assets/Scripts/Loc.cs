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
        ["menu.controls"] = ("Left click: give candy     Right click: send away\nSpace / middle click / wheel: palm blast (blows off a sheet, knocks off a prop)",
            "좌클릭: 사탕 주기     우클릭: 쫓아내기\n스페이스 / 휠클릭 / 휠: 손바닥 날리기 (시트를 날리고 소품을 떨어뜨림)"),

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
    };

    public static bool Has(string key) => Table.ContainsKey(key);
}
