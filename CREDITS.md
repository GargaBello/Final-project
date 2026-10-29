# Credits

## Art

### Kenney — Tiny Town (CC0 1.0)
https://kenney.nl/assets/tiny-town

Used for: grass and dirt tiles, fences, complete 16x32 trees, bush, mushrooms, sprout, 32 house roof/wall/door/window components, and 21 castle wall/arch/tower components.
License: Creative Commons Zero (public domain). Attribution is not required; credited here so the source stays traceable.

### Kenney — Tiny Farm (CC0 1.0)
https://kenney.nl/assets/tiny-farm

Used for: sheep, cow and chicken sprites, sunflower, seedling, dotted bush, small rocks and rock pile, 12 barn beam/wall/door/window/crate components, and 13 green farmhouse roof components.
License: Creative Commons Zero (public domain).

Full original tilesheets are stored in `Assets/Art/Source/`. Both are now fully sliced into 132 sprites each (16x16 pixels, 16 pixels per unit, point filtering). The combined `Kenney` palette contains all 264 cells: Tiny Town on the left and Tiny Farm on the right, separated by two empty columns. Each retains its original 12-column by 11-row layout. Paintable tile assets are stored in `Assets/Tiles/Tiny Town/` and `Assets/Tiles/Tiny Farm/` and reference the source-sheet sprites directly. Names use zero-based row/column coordinates counted from the top left. Multi-cell trees and buildings can be selected and painted together in their original arrangement. Existing curated sprites, tiles, and the Farm palette remain available for current scene references.

Building components are stored in `Assets/Art/Environment/Buildings/`, with paintable tiles in `Assets/Tiles/Buildings/`. Each is an unchanged 16x16 crop imported at 16 pixels per unit, without rotation or scaling. In the Farm palette, the two house sets and barn set occupy rows -10 through -13, arranged in the same order as the source sheets so their edges join correctly. Tiny Town uses source columns 0-7, rows 4-7; Tiny Farm uses columns 6-8, rows 7-10 (zero-based, from the top left). These tiles provide artwork only; collision and building interaction can be added when buildings are placed in a scene.

## Audio

### Sheep Baa — AntumDeluge / original recording by mikewest (CC0 1.0)
https://opengameart.org/content/sheep-baa

Original recording: https://freesound.org/people/mikewest/sounds/414342/

File: `Assets/Audio/Sheep/sheep_baa.ogg` (mono, 44.1 kHz).
Used for: player bleat and prototype NPC responses, with small pitch variations.
License: Creative Commons Zero. Downloaded from the OpenGameArt file linked above.




Green farmhouse roof tiles occupy Farm palette columns 15-17, rows -9 through -13, including the single ridge cap at column 16, row -9. They use Tiny Farm source column 10, row 6 (ridge cap), plus columns 9-11, rows 7-10. Castle tiles occupy columns 0-6, rows -16 through -18 and use Tiny Town source columns 0-6, rows 8-10. Coordinates are zero-based; source rows count from the top. Existing palette tiles are preserved.
