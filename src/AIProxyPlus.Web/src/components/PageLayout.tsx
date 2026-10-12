import type { ReactNode } from 'react';
import { makeStyles, tokens, Title2 } from '@fluentui/react-components';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
});

export function PageHeader({ title, actions }: { title: string; actions?: ReactNode }) {
  const styles = useStyles();
  return (
    <div className={styles.header}>
      <Title2>{title}</Title2>
      {actions}
    </div>
  );
}

export function PageContainer({ children }: { children: ReactNode }) {
  const styles = useStyles();
  return <div className={styles.root}>{children}</div>;
}
