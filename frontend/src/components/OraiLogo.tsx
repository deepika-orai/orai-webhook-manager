"use client";

import React from "react";
import Image from "next/image";

interface OraiLogoProps {
  className?: string;
  imageClassName?: string;
  priority?: boolean;
  alt?: string;
}

/**
 * Theme-aware, crisp transparent ORAI logo component.
 * Renders official brand assets seamlessly across Light and Dark themes
 * using zero-flash Tailwind CSS classes (`block dark:hidden` / `hidden dark:block`).
 * Accessibility: Ensures alt text is provided semantically without duplicate announcements.
 */
export function OraiLogo({
  className = "",
  imageClassName = "",
  priority = true,
  alt = "ORAI Conversational AI Platform",
}: OraiLogoProps) {
  return (
    <div
      role="img"
      aria-label={alt}
      className={`relative inline-flex items-center max-w-full select-none ${className}`}
    >
      {/* Light Theme Logo: visible when html does not have .dark */}
      <Image
        src="/branding/orai-logo-light.png"
        alt=""
        aria-hidden="true"
        width={1400}
        height={490}
        priority={priority}
        className={`block dark:hidden object-contain max-w-full pointer-events-none ${imageClassName}`}
      />

      {/* Dark Theme Logo: visible when html has .dark */}
      <Image
        src="/branding/orai-logo-dark.png"
        alt=""
        aria-hidden="true"
        width={1400}
        height={490}
        priority={priority}
        className={`hidden dark:block object-contain max-w-full pointer-events-none ${imageClassName}`}
      />
    </div>
  );
}
