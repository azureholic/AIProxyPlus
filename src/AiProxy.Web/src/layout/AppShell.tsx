import { Fragment, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  Hamburger,
  NavDrawer,
  NavDrawerBody,
  NavDrawerHeader,
  NavItem,
  NavSectionHeader,
  Title3,
  Toolbar,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { navSections, settingsRoute } from '../routes/routes';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    height: '100vh',
    width: '100%',
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingHorizontalM,
  },
  content: {
    flex: 1,
    overflow: 'auto',
  },
});

export function AppShell({ children }: { children: React.ReactNode }) {
  const styles = useStyles();
  const [open, setOpen] = useState(true);
  const location = useLocation();
  const navigate = useNavigate();

  return (
    <div className={styles.root}>
      <NavDrawer
        open={open}
        type="inline"
        selectedValue={location.pathname}
        onNavItemSelect={(_, data) => navigate(String(data.value))}
      >
        <NavDrawerHeader>
          <Toolbar className={styles.header}>
            <Hamburger onClick={() => setOpen(!open)} />
            <Title3>AI Proxy</Title3>
          </Toolbar>
        </NavDrawerHeader>
        <NavDrawerBody>
          {navSections.map((section) => (
            <Fragment key={section.title}>
              <NavSectionHeader>{section.title}</NavSectionHeader>
              {section.routes.map((route) => (
                <NavItem key={route.path} icon={route.icon} value={route.path}>
                  {route.label}
                </NavItem>
              ))}
            </Fragment>
          ))}
          <NavItem icon={settingsRoute.icon} value={settingsRoute.path}>
            {settingsRoute.label}
          </NavItem>
        </NavDrawerBody>
      </NavDrawer>

      {!open && <Hamburger onClick={() => setOpen(true)} />}

      <div className={styles.content}>{children}</div>
    </div>
  );
}
