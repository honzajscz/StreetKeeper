# StreetFlow — Sběrná appka (MAUI + ALPR), iterace 1 — Návrh povrchu

- **Datum:** 2026-06-16
- **Stav:** Návrh ke schválení
- **Nadřazený dokument:** [StreetFlow — Návrh / PRD](./2026-06-16-streetflow-design.md)
- **Povrch:** Sběrná appka (.NET MAUI, Android first)

## 1. Účel a rozsah iterace

Sběrná appka je první stavěný povrch StreetFlow, protože nese **největší technické
riziko celého projektu** — on-device čtení SPZ (ALPR/OCR) z mobilu v reálných
podmínkách. Cíl iterace 1 je ověřit v terénu celý on-device řetězec:

```
kamera → detekce plotny → OCR → normalizace → hash → lokální záznam → (stub odeslání)
```

S vědomým odložením všeho, co na tomto riziku nezávisí: reálný zápis do Supabase,
klasifikace typu vozidla, iOS, vícebodová orchestrace.

## 2. Platforma a technologie

- **.NET MAUI**, **Android first**.
- **Verze .NET:** baseline = **nejnovější stabilní .NET (10)** kvůli spolehlivosti
  MAUI tooling/workloadů. **.NET 11 preview** se zváží jen tehdy, pokud během
  setupu/evalu vyjde konkrétní feature s reálným přínosem pro tuto appku — ne
  preview kvůli preview.
- **ONNX Runtime** (`Microsoft.ML.OnnxRuntime`) pro on-device inferenci.
- **Kamera:** živý náhled + vzorkování snímků (MAUI camera / platformní API).

## 3. Architektura — komponenty

Systém je rozdělen na malé, samostatně testovatelné jednotky. Čisté logické
jednotky (point detector, normalizer, hasher, config parsing) se vyvíjejí přes TDD;
ML pipeline se ověřuje přes eval krok a terénní test.

1. **Campaign config provider (`ICampaignConfigSource`)**
   - Načte konfiguraci kampaně (název, polygon oblasti, hraniční body) podle `campaign_id`.
   - Iterace 1: čte z **lokálního JSON / mock zdroje**. Reálný Supabase zdroj přijde
     s povrchem „Supabase schéma".

2. **Point detector (GPS)**
   - Z aktuální GPS pozice a seznamu bodů kampaně určí, na kterém hraničním bodě
     dobrovolník stojí (nejbližší bod v daném rádiusu).
   - Čistá, plně testovatelná logika.

3. **Camera feed**
   - Živý náhled na ulici + vzorkování snímků pro inferenci.

4. **ALPR pipeline — dvoufázová**
   - **Detektor plotny:** YOLO-based ONNX (kandidáti vybráni v eval kroku).
   - **OCR znaků:** CRNN / PaddleOCR-rec ONNX.
   - **Eval krok (první krok implementace):** vyzkoušet 2–3 konkrétní kandidátní
     modely na vzorku **českých SPZ**, změřit přesnost ve verifikačním režimu (viz §4)
     a vybrat. Teprve pak plná integrace.

5. **Plate normalizer**
   - Sjednocení záměnných znaků (`I↔1`, `O↔0`, …), odstranění mezer, jednotná
     velikost písmen. Čistá funkce, TDD.

6. **Plate hasher**
   - Nevratný **SHA-256** hash *normalizované* značky, **se solí odvozenou z kampaně**
     (ochrana proti rainbow-table). Čistá funkce.
   - Hash se počítá ze *zobrazené normalizované* značky, takže po operátorské korekci
     (§4) se přepočítá.

7. **Color heuristic**
   - Dominantní barva auta z pixelů výřezu (heuristika, **bez ML modelu**).
   - `vehicle_type` zůstává `null` (přidá se v pozdější iteraci).

8. **Local capture store**
   - Lokální úložiště všech záchytů na telefonu (app sandbox). Každý záznam:
     `{ local_id, fotka výřezu plotny, raw OCR text, normalizovaná značka,
        plate_hash, color, vehicle_type=null, timestamp, status, sync_state }`
     - `status`: `valid | invalidated`
     - `sync_state`: stav odeslání anonymního záznamu (stub v iteraci 1).

9. **Verification & correction UI** *(verifikace je vždy zapnutá)*
   - Přehled záchytů; u každého **fotka výřezu + dekódovaná značka** pro posouzení
     správnosti OCR.
   - Operátor může:
     - **zneplatnit** záznam (`status → invalidated`) → vyřadí ho ze statistik;
     - **opravit značku** (ruční přepis) → znovu projde **normalizací** → přepočítá
       se **`plate_hash`**; záznam se označí jako opravený;
     - **smazat / vymazat celý lokální log** (fotky) jedním tlačítkem.

10. **Record sink → record store + sync (`IRecordSink`)**
    - Není fire-and-forget. Anonymní záznamy jdou nejdřív do **local capture store**;
      odtud se **synchronizují** do Supabase.
    - Iterace 1: **stub** — řeší jen lokální stranu (zápis do store + označení
      `sync_state`). Reálná Supabase sync logika, včetně **update/invalidace už
      odeslaného záznamu** po korekci, přijde s povrchem „Supabase schéma".

## 4. Privacy hranice (zpřesněná zero-retention)

Appka **vždy** drží lokálně na telefonu dvojici `{fotka výřezu plotny, dekódovaná
značka}` kvůli kontrole správnosti. **Tato data nikdy neopustí telefon.**

Ven (do Supabase) jde **pouze anonymní záznam**:

```json
{
  "campaign": "<id kampaně>",
  "point_id": "<id hraničního bodu>",
  "plate_hash": "<nevratný hash normalizované SPZ>",
  "color": "<barva auta>",
  "vehicle_type": null,
  "timestamp": "<čas záznamu>"
}
```

„Zero-retention" tedy v této appce znamená: **nic osobního (fotka, text SPZ) neopustí
zařízení**. Lokálně se fotky drží kvůli verifikaci a jdou kdykoli hromadně smazat.
Konkrétní SPZ nikdy nikdo mimo telefon dobrovolníka nevidí.

## 5. Datový tok

```
[kamera] → [ALPR: detekce plotny → OCR] → raw text
                                            │
                              [normalizace] → normalizovaná značka
                                            │
                                 [hash + sůl kampaně] → plate_hash
                                            │
[fotka výřezu] ──────────────┐              │   [color heuristika]
                             ▼              ▼              │
                       [LOCAL CAPTURE STORE] ◄─────────────┘
                             │        ▲
            (operátor: zneplatnit /   │ (verifikace vždy zapnutá)
             opravit → re-normalizace │
             → re-hash)               │
                             ▼        │
                     [IRecordSink sync] ──(stub v it.1)──► [Supabase: anonymní záznam]
                             ▲
                    fotka/text SPZ sem NIKDY nejdou
```

## 6. Testovací strategie

- **TDD** na čistých jednotkách: point detector (GPS → bod), plate normalizer,
  plate hasher (vč. soli a re-hashe po korekci), config parsing, logika
  zneplatnění/korekce ve store.
- **ALPR pipeline** se neunit-testuje na přesnost; ověřuje se:
  - **eval krokem** (kandidátní modely na vzorku českých SPZ),
  - **manuálním terénním testem** ve verifikačním režimu (měření reálné přesnosti čtení).

## 7. Mimo rozsah iterace 1

- Reálný Supabase zápis a sync logika (update/invalidace odeslaných záznamů).
- Klasifikace **typu vozidla** (`vehicle_type`).
- **iOS**.
- Orchestrace více bodů / více dobrovolníků.

## 8. Otevřené otázky

- Konkrétní volba ALPR detektoru plotny a OCR modelu (rozhodne eval krok).
- Přesná podoba soli odvozené z kampaně (vstup z konfigurace kampaně).
- Rádius pro GPS přiřazení k hraničnímu bodu (kalibrace v terénu).
- Frekvence vzorkování snímků z kamery vs. spotřeba/výkon (ladění v terénu).
