import { Component, Input } from '@angular/core';

import {
  Movement,
  MOVEMENT_EVENT_ICONS,
  MOVEMENT_EVENT_LABELS,
  MovementEventType,
} from '../../../core/models/tracking.models';

/** A vertical timeline of a container's movement events, oldest first. */
@Component({
  selector: 'app-timeline',
  templateUrl: './timeline.component.html',
  styleUrls: ['./timeline.component.scss'],
})
export class TimelineComponent {
  @Input() events: Movement[] = [];

  label(type: MovementEventType): string {
    return MOVEMENT_EVENT_LABELS[type];
  }

  icon(type: MovementEventType): string {
    return MOVEMENT_EVENT_ICONS[type];
  }

  isLatest(index: number): boolean {
    return index === this.events.length - 1;
  }

  trackById(_: number, event: Movement): number {
    return event.id;
  }
}
