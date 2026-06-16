# StreetFlow — Návrh / PRD

- **Datum:** 2026-06-16
- **Stav:** Návrh ke schválení
- **Pracovní název:** StreetFlow

## 1. Účel a publikum

StreetFlow má **dokázat, že sledovanou oblastí (ulice/čtvrť) auta převážně projíždějí**
(tranzit / zkratka / objízdná trasa), místo aby tam měla cíl.

- **Primární publikum:** média a veřejnost.
- **Výstup** musí být proto vizuální, srozumitelný a sdílitelný, s jedním úderným
  „headline" číslem (např. *„68 % aut naší ulicí jen projíždí"*).
- Výstup je dostatečně důvěryhodný i pro tlak na radnici, ale primárně cílí na
  veřejný/mediální příběh, ne na soudně-průkaznou evidenci.

## 2. Princip a architektura (bez vlastního backendu)

Tvrdé omezení: **žádná naše serverová část. Vše běží na klientu** a používá
technologie zdarma s nulovým/minimálním setupem.

Čtyři povrchy:

1. **Organizer Console — Blazor WASM, statický hosting.**
   Stránka pro založení kampaně. Organizátor zde **vygeneruje `campaign_id`**
   (klient-side, např. GUID), v mapě (Leaflet + OpenStreetMap) **nakreslí polygon
   oblasti** a označí **hraniční body** (vjezdy/výjezdy). Konfiguraci kampaně uloží
   do Supabase, odkud si ji načítají ostatní povrchy.

2. **Sběrná appka — .NET MAUI (nejdřív Android).**
   Běží u dobrovolníka na pozorovacím stanovišti (telefon na stativu / u okna,
   míří na ulici). Načte si konfiguraci kampaně podle `campaign_id`; podle **GPS**
   pozná, na kterém hraničním bodě dobrovolník stojí. Čte SPZ projíždějících aut
   pomocí **OCR on-device** (ONNX Runtime + ALPR model). Fotka ani text značky
   telefon nikdy neopustí.

3. **Sdílená DB — Supabase (free tier).**
   Hostovaný Postgres + automatické REST API + realtime. Drží **konfiguraci kampaní**
   (oblast, body) a **anonymizované záznamy** průjezdů. Organizer Console a appka
   zapisují, dashboard čte. Přístup přes „anon" klíč, oddělený **klíč/identifikátor
   na kampaň**. Není to „náš" server (Backend-as-a-Service).

4. **Public Dashboard — Blazor WASM, statický hosting (např. GitHub Pages).**
   Načte záznamy ze Supabase, spočítá **párování a statistiky přímo v prohlížeči**,
   zobrazí headline číslo a vygeneruje sdílecí kartu. Mapa přes **Leaflet +
   OpenStreetMap** (později vizualizace toků).

Organizer Console a Public Dashboard jsou obě statické Blazor WASM stránky; mohou
být buď dvě samostatné statické nasazení, nebo dvě části jednoho statického webu.

```
[Organizer Console] --> [Supabase] : konfigurace kampaně (campaign_id, polygon, body)
       (Blazor WASM)         |
                             | konfigurace kampaně
                             v
[MAUI appka @ bod A]  --\
[MAUI appka @ bod B]  ---> [Supabase: anonymní záznamy] <--- [Public Dashboard]
   (OCR on-device,        (hash + barva/typ +                (párování + statistiky
    hash, zero-retention)  čas + bod + kampaň)                v prohlížeči, sdílecí karta)
```

## 3. Role

- **Organizátor:** v Organizer Console založí kampaň (vygeneruje `campaign_id`),
  v mapě nakreslí polygon oblasti a označí hraniční body (vjezdy/výjezdy), uloží
  konfiguraci do Supabase a rozdá dobrovolníkům identifikátor/klíč kampaně.
- **Dobrovolník:** přijde na přidělený bod; appka podle **GPS** pozná, na kterém bodě
  stojí, a spustí sběr. Jinak nedělá nic (sběr je automatický).
- **Veřejnost / média:** prohlíží veřejný dashboard a sdílecí kartu.

## 4. Datový tok a ochrana soukromí (zero-retention)

Na stanovišti pro každé zachycené auto:

1. OCR přečte registrační značku.
2. **Normalizace značky** — sjednocení záměnných znaků (např. `I`↔`1`, `O`↔`0`),
   odstranění mezer, jednotná velikost písmen. Bez tohoto kroku by se stejné auto
   kvůli překlepu OCR rozpadlo na dva různé hashe a tranzit by se „ztratil".
3. **Nevratný hash** normalizované značky (`plate_hash`).
4. Původní **fotka i text SPZ se okamžitě zahodí** — nikam se neukládají ani neodesílají.

Do Supabase se odešle pouze:

```json
{
  "campaign": "<id kampaně>",
  "point_id": "<id hraničního bodu>",
  "plate_hash": "<nevratný hash normalizované SPZ>",
  "color": "<barva auta>",
  "vehicle_type": "<typ: osobní/dodávka/...>",
  "timestamp": "<čas záznamu>"
}
```

Ven (do médií / na dashboard) jdou **jen agregované statistiky**. Konkrétní SPZ nikdy
nikdo nevidí. Tím je právní expozice (GDPR) minimální.

Vedle záznamů drží Supabase i **konfiguraci kampaně** (zapisuje ji Organizer Console,
čte appka i dashboard) — neobsahuje žádné osobní údaje:

```json
{
  "campaign_id": "<GUID kampaně>",
  "name": "<název kampaně>",
  "area_polygon": "<GeoJSON polygon sledované oblasti>",
  "points": [
    { "point_id": "<id>", "name": "<popis, např. 'sever — vjezd'>", "lat": 0.0, "lng": 0.0 }
  ]
}
```

## 5. Logika důkazu — definice „tranzitu"

Párování probíhá na dashboardu nad záznamy se **stejným `plate_hash`** napříč body.
Otisk auta (**barva + typ**) slouží jako **pojistka** proti chybnému čtení OCR
(shoda hashe potvrzená shodou barvy/typu).

Pravidla (kombinace časového okna a páru vjezd→výjezd):

- Stejný `plate_hash` na **dvou různých hraničních bodech** v krátkém časovém okně
  **X** → **tranzit** (auto oblastí projelo).
- `plate_hash` jen na **jednom bodě** (a/nebo se na výjezdech objeví až za mnoho hodin)
  → pravděpodobně **místní cíl** (auto v oblasti zastavilo/zaparkovalo).
- Práh **X** se nastaví na začátek **3–5 minut** a doladí se z reálných dat pilotu.

**Headline metrika:**

```
% projíždějících = tranzit / (tranzit + místní)
```

## 6. Rozsah MVP (mikro-pilot)

Cíl MVP: ověřit v terénu celý řetězec **založení kampaně → OCR → normalizace → hash
→ párování → headline číslo**.

- **Organizer Console v minimální podobě:** vygenerovat `campaign_id`, nakreslit
  polygon oblasti a označit 2 hraniční body, uložit konfiguraci do Supabase.
- **1 oblast, 2 body** (jeden vjezd + jeden výjezd).
- **2 dobrovolníci.**
- Sběr během několika dopravních špiček v rámci **jednoho týdne**.
- Výstup: **veřejný dashboard se headline číslem** + sdílecí karta.

### Mimo MVP (následující iterace)

- Mapa toků (šipky/proudy vjezd→výjezd, tloušťka = intenzita).
- Časový graf intenzity tranzitu přes den (profil špiček).
- 3–6 bodů, více dobrovolníků, delší sběr.
- Více oblastí / kampaní, plné role a správa kampaní.

## 7. Hlavní rizika a jejich ošetření

| Riziko | Dopad | Ošetření |
| --- | --- | --- |
| **Real-time OCR z mobilu** (světlo, déšť, rychlost) | Největší technické riziko celého projektu | Zvolit ověřený ALPR ONNX model; testovat čtení v terénu co nejdřív (spike před zbytkem MVP) |
| **Otrávení sdílené DB** falešnými/spam záznamy | Zkreslení statistik | Klíč/identifikátor na kampaň; jednoduchá validace záznamů; pro pilot přijatelné riziko |
| **Přesnost párování** (chybné OCR rozbije shodu hashů) | Podhodnocení tranzitu | Normalizace značky před hashem; otisk auta (barva/typ) jako pojistka |
| **Závislost na free tieru** (Supabase, GitHub Pages, OSM) | Limity/výpadky služby | Pilotní objem je malý; v PRD vědomě akceptováno |

## 8. Technologický souhrn

- **Organizer Console:** Blazor WASM, statický hosting, Leaflet + OpenStreetMap
  (kreslení oblasti a bodů), generování `campaign_id` klient-side.
- **Sběrná appka:** .NET MAUI (Android first), ONNX Runtime pro on-device ALPR/OCR.
- **Sdílená data:** Supabase (Postgres + REST + realtime, free tier, anon klíč).
- **Public Dashboard:** Blazor WASM, statický hosting (GitHub Pages), Leaflet + OpenStreetMap.
- **Vše na klientu**, žádný vlastní backend; nástroje zdarma s nulovým/minimálním setupem.

## 9. Otevřené otázky (k doladění při implementaci)

- Konkrétní hodnota prahu **X** (kalibrace z dat pilotu).
- Volba konkrétního ALPR/OCR ONNX modelu a jeho přesnost v reálných podmínkách.
- Detaily ochrany kampaně v Supabase (RLS pravidla, omezení zápisu).
- Forma sdílecí karty (rozměry, branding StreetFlow).
