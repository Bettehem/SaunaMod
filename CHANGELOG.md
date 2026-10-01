# Changelog

## 2.2.0

* Added **stone heat**: the stones heat up while the stove burns and cool down after the fire goes out
* Pouring water spends stone heat, gives less steam on cooler stones and is not possible when the stones are too cold
* The stove holds 5 wood; the pour cooldown is reduced to 5 seconds
* Hot stones redden, glow and softly light the sauna
* Sauna comfort now depends on stone heat instead of the fire
* New `Stove` config options: `MaxWood`, `HeatingSpeed`, `SteamDependsOnHeat` and `Glow`

## 2.1.0

* Added the **Too hot** status effect: steaming with clothes or armor on deals damage and blocks steam healing and Well steamed progress
* Skin now gradually reddens while steaming and cools back afterwards
* Fixed sauna piece recipes not being applied at startup
* Improved Finnish translations

## 2.0.1

* Mod description changed

## 2.0.0

A major sauna progression update.

* Added **Sauna whisks**, a new buildable sauna upgrade made from 5 Fine Wood and 1 Bronze Nail
* Added the **Sauna bucket with ladle**, a new buildable upgrade made from 5 Iron and 10 Fine Wood
* Sauna whisks now improve Well Steamed: they remove the recovery penalties from Wet and unlock the 15-minute warming tier
* Without whisks, Well Steamed tops out at 10 minutes; with whisks, it can reach 15 minutes
* Added a localized maximum-steam message when the player reaches the best warming tier available to the current sauna
* The sauna bucket increases the amount of steam released by a pour
* The bucket can now be infused with supported resistance meads; the next pour carries the mead through the sauna steam and shares its vanilla resistance effect with nearby sheltered players
* Supported infusions include Poison Resistance Mead, Frost Resistance Mead and Fire Resistance Barley Wine
* Resistance effects delivered through the sauna last 20% longer by default; the multiplier can be changed in the config
* Added conditional **Sauna Comfort**: whisks and bucket each add +1 Comfort, but only near a burning sauna stove while sheltered
* Sauna Comfort can be disabled from the config
* Added `nekitker.saunamod.cfg` with settings for Well Steamed timing, steam healing, steam detection and grace time, effect durations, mead duration, steam behaviour, cloud counts and limits, comfort, recipes and the in-game editor
* All SaunaMod configuration is synchronized from the host or dedicated server to clients
* Added configurable recipes for every buildable SaunaMod piece; both ingredient types and amounts can be changed
* Added an optional lightweight in-game editor for tuning and testing, enabled from the config and opened with **F7**
* Updated Well Steamed tooltips so whisk-specific benefits are shown only when the effect was earned from a sauna with whisks
* Added clearer bucket interaction messages, including feedback when a non-resistance mead is used
* Added full Finnish localization alongside English, Russian, German and Spanish
* Refined item names, descriptions and status-effect text across all supported languages
* Improved sauna whisk visuals by restoring the intended birch-leaf material and making the vegetation-based parts stable across different systems
* Sauna upgrades, mead infusion and synchronized settings are multiplayer-ready

## 1.1.3

* Returning to the steam no longer starts from scratch: the time you already have counts, so topping up a nearly full effect takes one short sit instead of three
* Well steamed no longer counts down while you are still in the steam room
* Steaming now requires shelter: a steam bath needs a roof over your head

## 1.1.2

* The stove now warms a wider area, closer to a campfire

## 1.1.1

* The stones now glow only while the stove is burning; once the fire dies they darken to coals

## 1.1.0

* Steam now rolls out low and wide and fills rooms evenly, large ones included
* One pour keeps the room steamy for about forty seconds — enough to get well steamed
* Steaming is more forgiving: stepping out of the steam for a moment no longer resets your progress

Everyone in a session must update: 1.1.0 is not compatible with 1.0.0.

## 1.0.0

First release.

* Sauna stove, buildable from the hammer (Furniture tab)
* Pour water with Shift + E to fill the room with steam
* Steaming and Well steamed status effects
* Steam gathers under a roof and does not choke you
* Full multiplayer synchronisation
* English, Russian, German and Spanish

