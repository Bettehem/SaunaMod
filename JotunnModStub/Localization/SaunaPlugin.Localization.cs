// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Nekitker
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace SaunaMod
{
    internal partial class SaunaPlugin
    {
        private void AddLocalization()
        {
            _loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Sauna stove" },
                { "piece_sauna_stove_desc", "A stone sauna stove. Pour on water and enjoy a proper steam." },
                { "piece_sauna_pour", "Pour water" },
                { "msg_sauna_pour", "Water hits the hot stones" },
                { "msg_sauna_notburning", "The stove is cold" },
                { "msg_sauna_cold_stones", "The stones are not hot enough" },
                { "piece_sauna_heat", "Stone heat" },
                { "msg_sauna_wait", "The water is still hissing on the stones" },
                { "msg_sauna_tier", "The heat sinks deeper" },
                { "msg_sauna_max_tier", "Now that’s a proper steam!" },
                { "msg_sauna_bucket_use_mead", "Pour in mead" },
                { "msg_sauna_bucket_choose_mead", "More than one resistance mead is available. Aim at the bucket and use the one you want from the hotbar" },
                { "msg_sauna_bucket_no_mead", "You have no supported resistance mead" },
                { "msg_sauna_bucket_resistance_only", "Only resistance mead is suitable for the sauna" },
                { "msg_sauna_bucket_contains", "Infused with" },
                { "msg_sauna_bucket_full", "The sauna bucket already contains mead" },
                { "msg_sauna_bucket_filled", "Added {0} to the sauna bucket" },
                { "msg_sauna_mead_aroma", "The aroma of {0} fills the sauna" },
                { "msg_sauna_too_hot", "Too Hot! Take your clothes off"},
                { "msg_sauna_too_hot_weapons", "Too Hot! Take your clothes and weapons off"},
                { "se_sauna_too_hot", "Too hot" },
                { "se_sauna_too_hot_tooltip", "You need to be naked in the sauna, so that you don't get too hot!" },
                { "se_sauna_too_hot_start", "You are feeling too hot!" },
                { "se_sauna_steaming", "Steaming" },
                { "se_sauna_steaming_tooltip", "The steam slowly restores health and eases whatever ails you." },
                { "se_sauna_steaming_start", "You step into the steam" },
                { "se_sauna_wellsteamed", "Well steamed" },
                { "se_sauna_wellsteamed_tooltip",
                  "The heat stays with you.\nYou do not feel the cold." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "The heat stays with you.\nYou do not feel the cold.\nSauna whisks make wetness harmless and unlock 15-minute warming." },
                { "se_sauna_wellsteamed_tooltip_towels",
                  "The heat stays with you.\nYou do not feel the cold.\nSauna whisks make wetness harmless and unlock 15-minute warming.\nWarm muscles climb steep slopes with less effort and slip less.\nFreezing hurts less." },
                { "se_sauna_wellsteamed_start", "You are well steamed" },
                { "piece_sauna_wrisks", "Sauna whisks" },
                { "piece_sauna_wrisks_desc", "A pair of birch sauna whisks. They help you steam properly and shrug off the discomfort of being wet." },
                { "piece_sauna_bucket", "Sauna bucket with ladle" },
                { "piece_sauna_bucket_desc", "Lets you throw more water on the stones and infuse the steam with mead, so you can enjoy its aroma and absorb its effects." },
                { "piece_sauna_towel_rack", "Sauna towel rack" },
                { "piece_sauna_towel_rack_desc", "Wolf-pelt towels on a wooden rail, a birch bundle on its hook and a shelf of folded cloth. No proper sauna is without one." }
            });

            _loc.AddTranslation("Russian", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Банная печь" },
                { "piece_sauna_stove_desc", "Каменная банная печь. Поддай пару и как следует пропарься." },
                { "piece_sauna_pour", "Поддать пару" },
                { "msg_sauna_pour", "Вода шипит на раскалённых камнях" },
                { "msg_sauna_notburning", "Печь не растоплена" },
                { "msg_sauna_cold_stones", "Камни недостаточно горячие" },
                { "piece_sauna_heat", "Жар камней" },
                { "msg_sauna_wait", "Вода всё ещё шипит на камнях" },
                { "msg_sauna_tier", "Жар проникает глубже" },
                { "msg_sauna_max_tier", "С лёгким паром!" },
                { "msg_sauna_bucket_use_mead", "Подлить медовуху" },
                { "msg_sauna_bucket_choose_mead", "У тебя несколько защитных медовух. Наведи на ведро и используй нужную медовуху с панели быстрого доступа" },
                { "msg_sauna_bucket_no_mead", "У тебя нет подходящей защитной медовухи" },
                { "msg_sauna_bucket_resistance_only", "Для бани подходит только защитная медовуха" },
                { "msg_sauna_bucket_contains", "Добавлено" },
                { "msg_sauna_bucket_full", "В банном ведре уже есть медовуха" },
                { "msg_sauna_bucket_filled", "В банное ведро добавлено: {0}" },
                { "msg_sauna_mead_aroma", "Аромат {0} наполняет парную" },
                { "msg_sauna_too_hot", "Слишком жарко! Сними одежду" },
                { "se_sauna_too_hot", "Слишком жарко" },
                { "se_sauna_too_hot_tooltip", "В бане парятся без одежды, иначе станет слишком жарко!" },
                { "se_sauna_too_hot_start", "Тебе слишком жарко!" },
                { "se_sauna_steaming", "Пропаривание" },
                { "se_sauna_steaming_tooltip", "Пар понемногу восстанавливает здоровье и выбивает из тебя всякую заразу." },
                { "se_sauna_steaming_start", "Ты зашёл в пар" },
                { "se_sauna_wellsteamed", "Пропарен" },
                { "se_sauna_wellsteamed_tooltip",
                  "Тепло держится в теле.\nХолод не берёт." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "Тепло держится в теле.\nХолод не берёт.\nБанные веники снимают неудобства от сырости и открывают прогрев на 15 минут." },
                { "se_sauna_wellsteamed_tooltip_towels",
                  "Тепло держится в теле.\nХолод не берёт.\nБанные веники снимают неудобства от сырости и открывают прогрев на 15 минут.\nРаспаренные мышцы легче держат на крутых склонах и меньше скользят.\nМороз ранит слабее." },
                { "se_sauna_wellsteamed_start", "Хорошо пропарился" },
                { "piece_sauna_wrisks", "Банные веники" },
                { "piece_sauna_wrisks_desc", "Пара берёзовых веников. Помогают как следует пропариться и не страдать от сырости." },
                { "piece_sauna_bucket", "Ведро с ковшом" },
                { "piece_sauna_bucket_desc", "Позволяет поддать больше воды и подлить медовуху, чтобы насладиться ароматным паром и получить её защитный эффект." },
                { "piece_sauna_towel_rack", "Вешалка для полотенец" },
                { "piece_sauna_towel_rack_desc", "Полотенца из волчьей шкуры на деревянной перекладине, берёзовый веник на крючке и полка со сложенной тканью. Какая же баня без неё." }
            });

            _loc.AddTranslation("German", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Saunaofen" },
                { "piece_sauna_stove_desc", "Ein steinerner Saunaofen. Mach einen Aufguss und schwitz dich ordentlich aus." },
                { "piece_sauna_pour", "Aufguss machen" },
                { "msg_sauna_pour", "Wasser zischt auf den heißen Steinen" },
                { "msg_sauna_notburning", "Der Ofen ist kalt" },
                { "msg_sauna_cold_stones", "Die Steine sind nicht heiß genug" },
                { "piece_sauna_heat", "Hitze der Steine" },
                { "msg_sauna_wait", "Das Wasser zischt noch auf den Steinen" },
                { "msg_sauna_tier", "Die Hitze dringt tiefer" },
                { "msg_sauna_max_tier", "Das war ein guter Aufguss!" },
                { "msg_sauna_bucket_use_mead", "Met dazugießen" },
                { "msg_sauna_bucket_choose_mead", "Du hast mehrere Widerstandsmete. Ziele auf den Eimer und benutze den gewünschten Met aus der Schnellleiste" },
                { "msg_sauna_bucket_no_mead", "Du hast keinen passenden Widerstandsmet" },
                { "msg_sauna_bucket_resistance_only", "Für die Sauna eignet sich nur Widerstandsmet" },
                { "msg_sauna_bucket_contains", "Enthält" },
                { "msg_sauna_bucket_full", "Im Saunaeimer ist bereits Met" },
                { "msg_sauna_bucket_filled", "{0} wurde in den Saunaeimer gegeben" },
                { "msg_sauna_mead_aroma", "Der Duft von {0} erfüllt die Sauna" },
                { "msg_sauna_too_hot", "Zu heiß! Zieh deine Kleidung aus" },
                { "se_sauna_too_hot", "Zu heiß" },
                { "se_sauna_too_hot_tooltip", "In der Sauna musst du nackt sein, sonst wird es dir zu heiß!" },
                { "se_sauna_too_hot_start", "Dir ist zu heiß!" },
                { "se_sauna_steaming", "Saunadampf" },
                { "se_sauna_steaming_tooltip", "Der Dampf stellt langsam Gesundheit wieder her und lindert, was dich plagt." },
                { "se_sauna_steaming_start", "Du trittst in den Dampf" },
                { "se_sauna_wellsteamed", "Gut durchgewärmt" },
                { "se_sauna_wellsteamed_tooltip",
                  "Die Wärme bleibt im Körper.\nKälte macht dir nichts aus." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "Die Wärme bleibt im Körper.\nKälte macht dir nichts aus.\nBirkenquasten machen Nässe harmlos und schalten 15 Minuten Wärme frei." },
                { "se_sauna_wellsteamed_tooltip_towels",
                  "Die Wärme bleibt im Körper.\nKälte macht dir nichts aus.\nBirkenquasten machen Nässe harmlos und schalten 15 Minuten Wärme frei.\nWarme Muskeln erklimmen steile Hänge müheloser und rutschen weniger.\nFrost schadet dir weniger." },
                { "se_sauna_wellsteamed_start", "Du bist gut durchgewärmt" },
                { "piece_sauna_wrisks", "Saunabirkenquaste" },
                { "piece_sauna_wrisks_desc", "Ein Paar Birkenquasten. Sie helfen dir, dich ordentlich durchzuwärmen, und Nässe macht dir nichts mehr aus." },
                { "piece_sauna_bucket", "Saunaeimer mit Kelle" },
                { "piece_sauna_bucket_desc", "Damit kannst du mehr Wasser aufgießen und Met hinzufügen, um den aromatischen Dampf zu genießen und seine Schutzwirkung aufzunehmen." },
                { "piece_sauna_towel_rack", "Sauna-Handtuchhalter" },
                { "piece_sauna_towel_rack_desc", "Handtücher aus Wolfsfell an einer Holzstange, ein Birkenbündel am Haken und ein Brett mit gefaltetem Stoff. Keine richtige Sauna kommt ohne aus." }
            });

            _loc.AddTranslation("Spanish", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Estufa de sauna" },
                { "piece_sauna_stove_desc", "Una estufa de sauna de piedra. Echa agua sobre las piedras y date un buen baño de vapor." },
                { "piece_sauna_pour", "Echar agua" },
                { "msg_sauna_pour", "El agua sisea sobre las piedras calientes" },
                { "msg_sauna_notburning", "La estufa está fría" },
                { "msg_sauna_cold_stones", "Las piedras no están lo bastante calientes" },
                { "piece_sauna_heat", "Calor de las piedras" },
                { "msg_sauna_wait", "El agua todavía chisporrotea sobre las piedras" },
                { "msg_sauna_tier", "El calor penetra más hondo" },
                { "msg_sauna_max_tier", "¡Eso sí que es un buen baño de vapor!" },
                { "msg_sauna_bucket_use_mead", "Añadir hidromiel" },
                { "msg_sauna_bucket_choose_mead", "Tienes varios hidromieles de resistencia. Apunta al cubo y usa el que quieras desde la barra rápida" },
                { "msg_sauna_bucket_no_mead", "No tienes un hidromiel de resistencia compatible" },
                { "msg_sauna_bucket_resistance_only", "Solo el hidromiel de resistencia sirve para la sauna" },
                { "msg_sauna_bucket_contains", "Contiene" },
                { "msg_sauna_bucket_full", "El cubo de sauna ya contiene hidromiel" },
                { "msg_sauna_bucket_filled", "Has añadido {0} al cubo de sauna" },
                { "msg_sauna_mead_aroma", "El aroma de {0} llena la sauna" },
                { "msg_sauna_too_hot", "¡Demasiado calor! Quítate la ropa" },
                { "se_sauna_too_hot", "Demasiado calor" },
                { "se_sauna_too_hot_tooltip", "En la sauna hay que estar desnudo, ¡si no, pasarás demasiado calor!" },
                { "se_sauna_too_hot_start", "¡Tienes demasiado calor!" },
                { "se_sauna_steaming", "Baño de vapor" },
                { "se_sauna_steaming_tooltip", "El vapor restaura poco a poco la salud y te hace sudar cualquier mal." },
                { "se_sauna_steaming_start", "Entras en el vapor" },
                { "se_sauna_wellsteamed", "Bien templado" },
                { "se_sauna_wellsteamed_tooltip",
                  "El calor permanece en tu cuerpo.\nEl frío no te afecta." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "El calor permanece en tu cuerpo.\nEl frío no te afecta.\nLos ramos de sauna anulan las molestias de estar mojado y desbloquean 15 minutos de calor." },
                { "se_sauna_wellsteamed_tooltip_towels",
                  "El calor permanece en tu cuerpo.\nEl frío no te afecta.\nLos ramos de sauna anulan las molestias de estar mojado y desbloquean 15 minutos de calor.\nLos músculos calientes suben pendientes empinadas con menos esfuerzo y resbalan menos.\nLa congelación te daña menos." },
                { "se_sauna_wellsteamed_start", "Has entrado bien en calor" },
                { "piece_sauna_wrisks", "Ramos de abedul para sauna" },
                { "piece_sauna_wrisks_desc", "Un par de ramos de abedul. Te ayudan a darte un buen baño de vapor y a no sufrir las molestias de estar mojado." },
                { "piece_sauna_bucket", "Cubo de sauna con cucharón" },
                { "piece_sauna_bucket_desc", "Permite echar más agua sobre las piedras y añadir hidromiel para disfrutar de su aroma en el vapor y absorber su efecto protector." },
                { "piece_sauna_towel_rack", "Toallero de sauna" },
                { "piece_sauna_towel_rack_desc", "Toallas de piel de lobo en una barra de madera, un manojo de abedul en su gancho y un estante con tela doblada. Ninguna sauna que se precie está sin él." }
            });

            _loc.AddTranslation("Finnish", new Dictionary<string, string>
            {
                { "piece_sauna_stove", "Kiuas" },
                { "piece_sauna_stove_desc", "Kivinen kiuas. Heitä löylyä ja nauti kunnon saunasta." },
                { "piece_sauna_pour", "Heitä löylyä" },
                { "msg_sauna_pour", "Vesi sihahtaa kuumille kiville" },
                { "msg_sauna_notburning", "Kiuas on kylmä" },
                { "msg_sauna_cold_stones", "Kivet eivät ole tarpeeksi kuumia" },
                { "piece_sauna_heat", "Kivien kuumuus" },
                { "msg_sauna_wait", "Vesi sihisee vielä kivillä" },
                { "msg_sauna_tier", "Lämpö painuu syvemmälle" },
                { "msg_sauna_max_tier", "Nyt tuli kunnon löylyt!" },
                { "msg_sauna_bucket_use_mead", "Kaada simaa kiuluun" },
                { "msg_sauna_bucket_choose_mead", "Sinulla on useita vastustuskykyä antavia simoja. Tähtää kiuluun ja käytä haluamaasi pikapalkista" },
                { "msg_sauna_bucket_no_mead", "Sinulla ei ole sopivaa vastustussimaa" },
                { "msg_sauna_bucket_resistance_only", "Vain vastustuskykyä antava sima sopii saunaan" },
                { "msg_sauna_bucket_contains", "Sisältää" },
                { "msg_sauna_bucket_full", "Saunakiulussa on jo simaa" },
                { "msg_sauna_bucket_filled", "{0} kaadettiin saunakiuluun" },
                { "msg_sauna_mead_aroma", "Höyryssä tuoksuu {0}" },
                { "msg_sauna_too_hot", "Liian Kuuma! Ota vaatteet pois"},
                { "msg_sauna_too_hot_weapons", "Liian Kuuma! Ota vaatteet ja aseet pois"},
                { "se_sauna_too_hot", "Liian kuuma" },
                { "se_sauna_too_hot_tooltip", "Saunassa täytyy olla alasti, jottei tule liian kuuma!" },
                { "se_sauna_too_hot_start", "Sinulla on liian kuuma!" },
                { "se_sauna_steaming", "Löylyissä" },
                { "se_sauna_steaming_tooltip", "Löyly palauttaa hiljalleen terveyttä ja karkottaa kolotukset." },
                { "se_sauna_steaming_start", "Astut löylyihin" },
                { "se_sauna_wellsteamed", "Hyvin saunottu" },
                { "se_sauna_wellsteamed_tooltip",
                  "Lämpö pysyy kehossa.\nKylmä ei tunnu missään." },
                { "se_sauna_wellsteamed_tooltip_whisks",
                  "Lämpö pysyy kehossa.\nKylmä ei tunnu missään.\nKoivuvihdat poistavat märkyyden haitat ja avaavat 15 minuutin lämmön." },
                { "se_sauna_wellsteamed_tooltip_towels",
                  "Lämpö pysyy kehossa.\nKylmä ei tunnu missään.\nKoivuvihdat poistavat märkyyden haitat ja avaavat 15 minuutin lämmön.\nLämpimät lihakset kiipeävät jyrkkiä rinteitä kevyemmin ja liukuvat vähemmän.\nPaleltuminen satuttaa vähemmän." },
                { "se_sauna_wellsteamed_start", "Olet lämmin läpikotaisin" },
                { "piece_sauna_wrisks", "Koivuvihdat" },
                { "piece_sauna_wrisks_desc", "Pari koivuvihtaa. Niillä saat kunnon löylyt, eikä märkyys enää haittaa." },
                { "piece_sauna_bucket", "Saunakiulu ja kauha" },
                { "piece_sauna_bucket_desc", "Tällä saat heitettyä enemmän löylyä ja voit lisätä simaa, jolloin sen tuoksu ja suojaava vaikutus kulkevat höyryn mukana." },
                { "piece_sauna_towel_rack", "Saunan pyyheteline" },
                { "piece_sauna_towel_rack_desc", "Sudennahkapyyhkeitä puisessa orressa, koivuvihta koukussa ja hylly taiteltua kangasta. Kunnon saunasta sellainen ei puutu." }
            });
        }
    }
}
