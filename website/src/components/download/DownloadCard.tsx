import type { Release } from "@/config/release";
import { downloadPage } from "@/content/download";
import { AnchorButton } from "@/components/ui/Button";
import { WindowsIcon } from "@/components/ui/icons";
import { PlatformNotice } from "@/components/download/PlatformNotice";
import { SharedElement } from "@/components/motion/SharedElement";
import { sharedElement } from "@/lib/transitions";

export function DownloadCard({ release }: { release: Release }) {
  return (
    <div className="flex flex-col items-center gap-4">
      <SharedElement name={sharedElement.downloadButton}>
        <AnchorButton href={release.url} download={release.fileName}>
          <WindowsIcon width={16} height={16} />
          Download for {release.platform}
        </AnchorButton>
      </SharedElement>
      <p className="text-sm text-balance text-muted">
        Version {release.version} · Free · {release.requirements}
      </p>
      <PlatformNotice message={downloadPage.unsupportedNotice} />
    </div>
  );
}
