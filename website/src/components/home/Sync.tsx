import { sync } from "@/content/home";
import { DeviceShowcase } from "@/components/home/DeviceShowcase";
import { devicesId } from "@/components/home/OwnPhoto";
import { Section } from "@/components/ui/Section";

/**
 * The devices, a screen of their own right under the slogan they show: as big as the screen allows, never taller than
 * it, so they and their credit fit on one screen.
 */
export function Sync() {
  return (
    <Section id={devicesId} label={sync.label} className="overflow-x-clip">
      <div className="mx-auto w-full max-w-[88rem] px-4 sm:px-8">
        <div className="mx-auto w-[min(100%,calc((100svh-15rem)*16/9))] min-w-[min(100%,40rem)]">
          <DeviceShowcase />
        </div>
      </div>
    </Section>
  );
}
