import { Outlet } from 'react-router-dom';
import { SiteFooter } from './SiteFooter.js';
import { SiteHeader } from './SiteHeader.js';
import '../../styles/landing.css';
import '../../styles/module-pages.css';

/**
 * Chrome for the module pages — header, page, footer.
 *
 * <b>A layout route rather than a wrapper each page renders.</b> The header
 * and footer are then mounted once for the whole group, so moving from the
 * document library to an article does not tear down and rebuild the header,
 * and an open account menu survives the navigation. It also means the next
 * module page gets its chrome by being listed here and in no other way.
 */
export function PublicShell() {
  return (
    <div className="landing">
      <SiteHeader />
      <main>
        <Outlet />
      </main>
      <SiteFooter />
    </div>
  );
}
