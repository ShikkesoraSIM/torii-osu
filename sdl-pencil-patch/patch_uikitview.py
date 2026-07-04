#!/usr/bin/env python3
# torii/ios: patchea SDL_uikitview.m para usar coalescedTouchesForTouch -> recupera el 240hz del
# Apple Pencil (que SDL hoy colapsa a ~120). el metodo touchesMoved: es el unico lugar donde tenemos
# el UIEvent (necesario para coalescedTouchesForTouch:), asi que ahi expandimos las sub-muestras y
# reenviamos una por una, tanto dedo como pen. detalles no negociables:
#   - fingerId SIEMPRE el UITouch ORIGINAL (identidad estable), no el sample coalesced
#   - timestamp POR sample ([sample timestamp]) -> reconstruye la cadencia de 240hz
#   - fallback @[touch] si coalesced viene vacio
# base: libsdl-org/SDL @ f31ca02 (= ppy.SDL3-CS 2026.520.0). touchesBegan/Ended NO se tocan.
#
# reemplaza el metodo ENTERO por balanceo de llaves (robusto al whitespace): busca la firma,
# encuentra el cuerpo contando { }, y lo pisa. no depende de espacios/lineas en blanco internos.
import sys, pathlib

SIG = "- (void)touchesMoved:(NSSet *)touches withEvent:(UIEvent *)event"

NEW = '''- (void)touchesMoved:(NSSet *)touches withEvent:(UIEvent *)event
{
    for (UITouch *touch in touches) {
        // torii: expandir las muestras coalesced (240hz del apple pencil / touch de alta cadencia).
        // uikit entrega solo el ultimo UITouch por defecto; el resto queda en el array coalesced.
        NSArray<UITouch *> *coalescedTouches = [event coalescedTouchesForTouch:touch];
        if (coalescedTouches.count == 0) {
            coalescedTouches = @[touch];
        }
        for (UITouch *sample in coalescedTouches) {
#if !defined(SDL_PLATFORM_TVOS)
            if (@available(iOS 13.0, *)) {
                if (sample.type == UITouchTypePencil) {
                    [self pencilMoving:sample];
                    continue;
                }
            }
            if (@available(iOS 13.4, *)) {
                if (sample.type == UITouchTypeIndirectPointer) {
                    [self indirectPointerMoving:sample];
                    continue;
                }
            }
#endif
            SDL_TouchDeviceType touchType = [self touchTypeForTouch:sample];
            SDL_TouchID touchId = [self touchIdForType:touchType];
            float pressure = [self pressureForTouch:sample];
            if (SDL_AddTouch(touchId, touchType, "") < 0) {
                continue;
            }
            CGPoint locationInView = [self touchLocation:sample shouldNormalize:YES];
            // fingerId anclado al touch ORIGINAL (identidad estable), timestamp POR sample (cadencia 240hz).
            SDL_SendTouchMotion(UIKit_GetEventTimestamp([sample timestamp]),
                                touchId, (SDL_FingerID)(uintptr_t)touch, sdlwindow,
                                locationInView.x, locationInView.y, pressure);
        }
    }
}'''


def main():
    if len(sys.argv) != 2:
        print("uso: patch_uikitview.py <ruta a SDL_uikitview.m>", file=sys.stderr)
        return 2
    p = pathlib.Path(sys.argv[1])
    src = p.read_text(encoding="utf-8")

    if "coalescedTouchesForTouch" in src:
        print("ya patcheado (coalescedTouchesForTouch presente); no hago nada.")
        return 0

    i = src.find(SIG)
    if i < 0:
        print("ERROR: no encontre la firma de touchesMoved:. cambio el SHA base de SDL?", file=sys.stderr)
        return 1
    if src.count(SIG) != 1:
        print(f"ERROR: {src.count(SIG)} firmas de touchesMoved:, esperaba 1.", file=sys.stderr)
        return 1

    b = src.find("{", i)
    if b < 0:
        print("ERROR: no encontre { tras la firma.", file=sys.stderr)
        return 1

    depth = 0
    j = b
    while j < len(src):
        c = src[j]
        if c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                break
        j += 1
    if depth != 0:
        print("ERROR: llaves desbalanceadas en touchesMoved:.", file=sys.stderr)
        return 1

    end = j + 1  # incluir la } final del metodo
    patched = src[:i] + NEW + src[end:]
    p.write_text(patched, encoding="utf-8")
    print("OK: touchesMoved: reemplazado por la version con coalesced touches (brace-balanced).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
