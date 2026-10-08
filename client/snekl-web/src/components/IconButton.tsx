import { type ButtonHTMLAttributes } from 'react';
import { Link } from 'react-router';

export type IconName =
  | 'check'
  | 'x'
  | 'trash'
  | 'pencil'
  | 'link'
  | 'users'
  | 'rows'
  | 'chevronUp'
  | 'chevronDown'
  | 'bookmark'
  | 'bookmarkOff'
  | 'eye'
  | 'eyeOff';

const iconProps = {
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round' as const,
  strokeLinejoin: 'round' as const,
  'aria-hidden': true,
};

export function Icon({ name }: { name: IconName }) {
  switch (name) {
    case 'check':
      return (
        <svg {...iconProps}>
          <path d="M5 12l5 5L20 7" />
        </svg>
      );
    case 'x':
      return (
        <svg {...iconProps}>
          <path d="M6 6l12 12M18 6L6 18" />
        </svg>
      );
    case 'trash':
      return (
        <svg {...iconProps}>
          <path d="M4 7h16M9 7V5h6v2M8 7l1 12h6l1-12" />
        </svg>
      );
    case 'pencil':
      return (
        <svg {...iconProps}>
          <path d="M4 20h4l10.5-10.5-4-4L4 16v4zM13.5 6.5l4 4" />
        </svg>
      );
    case 'link':
      return (
        <svg {...iconProps}>
          <path d="M10 13a5 5 0 0 0 7.5.1l1.4-1.4a5 5 0 0 0-7.1-7.1L10.5 6" />
          <path d="M14 11a5 5 0 0 0-7.5-.1L5.1 12.3a5 5 0 0 0 7.1 7.1L13.5 18" />
        </svg>
      );
    case 'users':
      return (
        <svg {...iconProps}>
          <path d="M16 19v-1a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v1" />
          <circle cx="10" cy="7" r="3" />
          <path d="M20 19v-1a3.5 3.5 0 0 0-2.5-3.3" />
          <path d="M16 4.1a3 3 0 0 1 0 5.8" />
        </svg>
      );
    case 'rows':
      return (
        <svg {...iconProps}>
          <path d="M4 7h16M4 12h16M4 17h16" />
        </svg>
      );
    case 'chevronUp':
      return (
        <svg {...iconProps}>
          <path d="M6 15l6-6 6 6" />
        </svg>
      );
    case 'chevronDown':
      return (
        <svg {...iconProps}>
          <path d="M6 9l6 6 6-6" />
        </svg>
      );
    case 'bookmark':
      return (
        <svg {...iconProps}>
          <path d="M7 4h10v16l-5-3-5 3z" />
        </svg>
      );
    case 'bookmarkOff':
      return (
        <svg {...iconProps}>
          <path d="M7 4h10v16l-5-3-5 3z" />
          <path d="M5 5l14 14" />
        </svg>
      );
    case 'eye':
      return (
        <svg {...iconProps}>
          <path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z" />
          <circle cx="12" cy="12" r="3" />
        </svg>
      );
    case 'eyeOff':
      return (
        <svg {...iconProps}>
          <path d="M3 3l18 18M10.5 6.1A11 11 0 0 1 12 5c6 0 10 7 10 7a18 18 0 0 1-4.2 4.6M6.5 6.5C4 8.4 2 12 2 12s4 7 10 7c1.4 0 2.7-.3 3.9-.8" />
          <path d="M9.9 9.9a3 3 0 0 0 4.2 4.2" />
        </svg>
      );
    default: {
      const _exhaustive: never = name;
      void _exhaustive;
      return null;
    }
  }
}

type IconControlProps = {
  icon: IconName;
  label: string;
  variant?: 'ghost' | 'primary' | 'danger';
  className?: string;
};

function iconClassName(variant: IconControlProps['variant'], className = '') {
  return `btn-icon btn-icon-${variant ?? 'ghost'}${className ? ` ${className}` : ''}`;
}

export function IconButton({
  icon,
  label,
  variant = 'ghost',
  className,
  type = 'button',
  ...props
}: IconControlProps & ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      {...props}
      type={type}
      className={iconClassName(variant, className)}
      aria-label={label}
      title={label}
    >
      <Icon name={icon} />
    </button>
  );
}

export function IconLink({
  to,
  icon,
  label,
  variant = 'ghost',
  className,
}: IconControlProps & { to: string }) {
  return (
    <Link to={to} className={iconClassName(variant, className)} aria-label={label} title={label}>
      <Icon name={icon} />
    </Link>
  );
}
