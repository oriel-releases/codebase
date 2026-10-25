/**
 * The refresh icons (the shuffle button, Home's Change now): they turn while a photo is on its way, and once it is
 * there they don't snap back to upright but ease on, at the speed they had, to a soft stop where they look upright.
 */

const turnMs = 900;
const running = new WeakMap<Element, Animation>();

/**
 * [period]: how far it turns before it looks the same again (the shuffle's two arrows: half a turn; Change now's one
 * arrow: a whole turn). It stops at the next such point.
 */
export function spin(icon: Element | null, on: boolean, period = 360): void {
  if (!icon || matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const current = running.get(icon);
  if (on) {
    if (current && current.effect?.getTiming().iterations === Infinity) return;
    // Picking up from wherever a previous settle had got to, so a quick second press doesn't jump.
    const from = current ? angleOf(current) : 0;
    current?.cancel();
    running.set(icon, icon.animate([{ transform: `rotate(${from}deg)` }, { transform: `rotate(${from + 360}deg)` }], { duration: turnMs, iterations: Infinity, easing: "linear" }));
    return;
  }
  if (!current || current.effect?.getTiming().iterations !== Infinity) return;
  const from = angleOf(current);
  current.cancel();
  // Once the photo is there it eases to a stop like a wheel let go of: a critically damped spring that starts at the
  // very speed it was turning and comes to rest, without a bounce, at the next point where it looks upright.
  let distance = period - (from % period);
  if (distance < 40) distance += period; // too close to stop softly: on to the next one
  const settle = icon.animate(settleFrames(from, distance), { duration: settleDuration(distance), easing: "linear" });
  running.set(icon, settle);
  settle.onfinish = () => {
    if (running.get(icon) === settle) running.delete(icon);
  };
}

/** Where a running turn is now, in degrees from upright (counting whole turns of the settle too). */
function angleOf(animation: Animation): number {
  const time = Number(animation.currentTime ?? 0);
  const timing = animation.effect?.getComputedTiming();
  if (timing?.iterations === Infinity) return ((time % turnMs) / turnMs) * 360 + startOf(animation);
  // A settle in progress: read the angle straight from the element.
  const target = (animation.effect as KeyframeEffect | null)?.target;
  if (!target) return 0;
  const m = new DOMMatrix(getComputedStyle(target).transform);
  return ((Math.atan2(m.b, m.a) * 180) / Math.PI + 360) % 360;
}

function startOf(animation: Animation): number {
  const first = (animation.effect as KeyframeEffect | null)?.getKeyframes()[0]?.transform;
  return Number(/rotate\((-?[\d.]+)deg\)/.exec(String(first ?? ""))?.[1] ?? 0);
}

/** The turning speed while it waits, in degrees per millisecond. */
const speed = 360 / turnMs;

/** How stiff the spring is for [distance] degrees: a longer way gets a softer one, so it never races ahead. */
const stiffness = (distance: number) => 0.007 * Math.sqrt(180 / distance);

/** Until it is within a fraction of a degree of upright. */
function settleDuration(distance: number): number {
  const w = stiffness(distance);
  const b = w * distance - speed;
  let t = 0;
  while (t < 2500 && (distance + b * t) * Math.exp(-w * t) > 0.15) t += 16;
  return t;
}

/**
 * The spring's path, a frame per 16 ms: what is left of the way, e(t) = (d + b t) e^(-w t), with b = w d - v so that
 * it starts at the turning speed v. b >= 0 for every distance it is given (see stiffness), so it never overshoots.
 */
function settleFrames(from: number, distance: number): Keyframe[] {
  const w = stiffness(distance);
  const b = w * distance - speed;
  const duration = settleDuration(distance);
  const frames: Keyframe[] = [];
  for (let t = 0; t < duration; t += 16) {
    frames.push({ offset: t / duration, transform: `rotate(${from + distance - (distance + b * t) * Math.exp(-w * t)}deg)` });
  }
  frames.push({ offset: 1, transform: `rotate(${from + distance}deg)` });
  return frames;
}
