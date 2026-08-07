import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Button, FluentProvider, Spinner, Text, makeStyles, tokens, webLightTheme } from '@fluentui/react-components';
import { AppShell } from './layout/AppShell';
import { navSections, settingsRoute, defaultRoutePath } from './routes/routes';
import { ModelDetailPage } from './pages/config/ModelDetailPage';
import { AuthProvider, useAuth } from './auth/AuthContext';

const useStyles = makeStyles({
  login: {
    minHeight: '100vh',
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalL,
    textAlign: 'center',
    backgroundColor: tokens.colorNeutralBackground2,
  },
  loginCopy: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
});

function AuthenticatedApp() {
  const styles = useStyles();
  const { session, providerName, loading, error, login } = useAuth();

  if (loading) {
    return <Spinner label="Loading session" />;
  }

  if (error) {
    return <div role="alert">{error}</div>;
  }

  if (!session) {
    return (
      <main className={styles.login}>
        <div className={styles.loginCopy}>
          <Text as="h1" size={700} weight="semibold">AI Proxy</Text>
          <Text>Administrator access is provided by {providerName}.</Text>
        </div>
        <Button appearance="primary" size="large" onClick={login}>
          Sign in with {providerName}
        </Button>
      </main>
    );
  }

  const allRoutes = [...navSections.flatMap((section) => section.routes), settingsRoute];

  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<Navigate to={defaultRoutePath} replace />} />
        <Route path="/config/models/:name" element={<ModelDetailPage />} />
        {allRoutes.map((route) => (
          <Route key={route.path} path={route.path} element={route.element} />
        ))}
      </Routes>
    </AppShell>
  );
}

function App() {
  return (
    <FluentProvider theme={webLightTheme}>
      <BrowserRouter>
        <AuthProvider>
          <AuthenticatedApp />
        </AuthProvider>
      </BrowserRouter>
    </FluentProvider>
  );
}

export default App;
