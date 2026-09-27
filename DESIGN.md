# Wright — designnotat

_Vintage Story-mod (modid `wright`, tidligere arbeidsnavn «Flyer»), målversjon 1.22.x. Startet 2026-09-27._

## Status

- **Steg 1 – ruller (ferdig, testet):** `EntityWright : EntityBoat`, overstyrer `updateBoatAngleAndMotion` (vanilla stopper når båten ikke svømmer). Item med `ItemBoat`-klassen, settes ut hvor som helst. Placeholder-modell = vanilla-flåten.
- **Steg 2 – løft/stallfart (ferdig, testet):** W/S = gass (blir liggende), Space = nesa opp, Ctrl = nesa ned. Seig akselerasjon, løft ∝ fart², stigning koster fart. Tuning i `attributes.flight` i entity-JSON. HUD-linje med gass/fart/løft/høyde.
- **Neste:** hvordan flyet bygges (se åpne spørsmål 2, 3 og 7).

Bygg + installer: `./deploy.sh` (kopierer til `VintagestoryData\Mods\wright`).

## Idé

Et tidlig propellfly i Wright-brødrenes ånd. Det løser et reelt hull i VS: det finnes ingen god måte å reise **langt** på.

- Flyet er en **entity** (som båten), ikke en blokk.
- Et item fra **mid til late game**. Det må craftes og bygges.
- Drivstoffet er **alkohol**.
- Flyet består av fire deler: **kropp, vinger, motor og styring**.
- Det letter ikke med en gang. Det trenger en **rullebane** for å bygge opp fart før det løfter seg.

Inspirasjon: en enkel Minecraft-flymod og Jaunt (library-en bak Pegasus-moden), men mer jordnær og mekanisk.

## Vanilla å bygge på (sjekket i 1.22.7)

- **`EntityBoat`** (VSSurvivalMod): har `ForwardSpeed`, `AngularVelocity`, `SpeedMultiplier` og `updateBoatAngleAndMotion(dt)`. Den leser styring fra passasjeren og roterer/flytter entityen. Dette er det naturlige utgangspunktet: arv, eller kopier mønsteret, og legg på høyde (pitch) og løft.
- **`EntityBehaviorSeatable` / `EntityBehaviorRideable`**: seter og montering. Jeg regner med at disse kan gjenbrukes uendret.
- **`EntityBoatConstruction`** (`boat-construction.json`): en **byggeplass i verden** med stages. Hver stage krever bestemte stacks og legger til modell-elementer. Det passer perfekt for «kropp → vinger → motor → styring» uten at vi trenger egen bygge-kode.
- **`ItemBoat`**: item som spawner entityen. Det trengs bare hvis flyet skal kunne plukkes opp og settes ned igjen.

## Flymodell (første skisse, bevisst enkel)

- Fart: gass øker `ForwardSpeed` mot en maksverdi, og friksjon trekker den ned igjen.
- Løft: `lift = k * speed²` (eventuelt forenklet). Under **stallfart** er løftet lavere enn tyngdekraften, så flyet ruller bare på bakken.
- Rullebane: det er ikke en egen blokk, bare at det trengs flat, fri bakke lang nok til å nå stallfart. Underlaget kan eventuelt påvirke friksjonen (gress vs. grus/plank).
- Styring: A/D gir krengning og sving, W/S gir pitch, og gass legges på en egen tast eller sprint.
- Drivstoff: tanken tømmes over tid mens motoren går. Tom tank betyr gliding, med synkrate avhengig av fart.
- Landing: for høy vertikal fart gir skade på flyet og/eller piloten.

## Åpne spørsmål

1. **Alkohol:** Hvilke væsker er gyldig drivstoff? Bare sterk sprit (destillert), eller all alkohol med ulik effekt? Hvordan fylles tanken: høyreklikk med bøtte eller flaske?
2. **Progresjon:** Hvilket metall krever motoren? Bronse, jern eller stål? Hva krever vingene: lin/stoff, lær, planker?
3. **Bygging:** Skal flyet bygges i verden som båten (construction site), eller crafte fire deler og sette dem sammen? Eller en blanding: delene craftes, og monteres på byggeplassen?
4. **Passasjerer:** Bare pilot, eller et ekstra sete?
5. **Holdbarhet:** Skal flyet slites og måtte repareres? Hva skjer ved krasj: ødelagt helt eller skadet?
6. **Vær:** Skal vind og storm (VS har værsimulering) påvirke flyet?
7. **Lagring:** Kan flyet plukkes opp som item, eller står det permanent der det er bygget?
8. **Modelltype:** VS-modell laget i VS Model Creator (`.json`-shape), som vanilla-båten.

## Foreslått første steg (liten, testbar bit)

Et **fly som ruller**: en entity basert på `EntityBoat` som kjører på bakken med W/A/S/D, har en placeholder-modell (en kasse holder) og spawnes med kommando. Ingen flyging, ingen drivstoff og ingen crafting ennå. Poenget er å bevise at entity, sete og styring fungerer før vi legger på løft.

Deretter, ett steg om gangen: løft/stallfart → drivstoff → byggeplass/oppskrifter → ekte modell.

## Teknisk oppsett (når vi starter på kode)

- C# kodemod på .NET 10 (samme som spillet 1.22). Mal: `dotnet new vsmod` fra VS sin offisielle template.
- Referanser fra `%APPDATA%\Vintagestory\` (`VintagestoryAPI.dll`, `Mods\VSSurvivalMod.dll`, `Mods\VSEssentials.dll`).
- Testing: kopier zip til `%APPDATA%\VintagestoryData\Mods`, og start en egen testverden i kreativ modus.
