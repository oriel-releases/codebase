import { sync } from "@/content/home";
import { DeviceShowcase } from "@/components/home/DeviceShowcase";
import { Section } from "@/components/ui/Section";
import { SectionHeading } from "@/components/ui/SectionHeading";

/**
 * One photo on every device, shown: the devices take as much room as the screen allows, never more than its height, so
 * the heading, the picture and its credit fit on one screen.
 */
export function Sync() {
  return (
    <Section labelledBy="sync">
      <div className="mx-auto flex w-full max-w-6xl flex-col items-center px-4 sm:px-6">
        <SectionHeading id="sync">{sync.title}</SectionHeading>
        <div className="mt-10 w-[min(100%,calc((100svh-16rem)*16/9))] min-w-[min(100%,36rem)]">
          <DeviceShowcase />
        </div>
      </div>
    </Section>
  );
}
