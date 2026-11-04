import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { authGuard, roleGuard } from './core/guards/auth.guards';

const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'bookings' },
  {
    path: 'login',
    loadChildren: () => import('./features/auth/auth.module').then(m => m.AuthModule),
  },
  {
    path: 'bookings',
    canActivate: [authGuard],
    loadChildren: () => import('./features/bookings/bookings.module').then(m => m.BookingsModule),
  },
  {
    path: 'containers',
    canActivate: [roleGuard('Ops')],
    loadChildren: () => import('./features/containers/containers.module').then(m => m.ContainersModule),
  },
  {
    path: 'tracking',
    canActivate: [authGuard],
    loadChildren: () => import('./features/tracking/tracking.module').then(m => m.TrackingModule),
  },
  {
    path: 'vessels',
    canActivate: [roleGuard('Ops')],
    loadChildren: () => import('./features/vessels/vessels.module').then(m => m.VesselsModule),
  },
  {
    path: 'voyages',
    canActivate: [roleGuard('Ops')],
    loadChildren: () => import('./features/voyages/voyages.module').then(m => m.VoyagesModule),
  },
  {
    path: 'tariffs',
    canActivate: [roleGuard('Ops')],
    loadChildren: () => import('./features/tariffs/tariffs.module').then(m => m.TariffsModule),
  },
  { path: '**', redirectTo: 'bookings' },
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule],
})
export class AppRoutingModule {}
