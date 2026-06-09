Long Submerged 10x+ v1.3.8

Paramètres utilisés :
- Air / atmosphère de base : capacité x1800
- Oxygen Consumption Per Character : divisé par 1800
- Discipline/fatigue sous l'eau : divisé par 15
- Batterie / Accumulators : x10
- EnergyUsage consommateurs hors ventilation/compresseurs : x0.1 dans les datasheets
- Mega Batterie runtime : slider 1-100, 100 coupe le drain electrique positif et maintient la ressource au maximum par defaut
- EnergyUsage recharge/production batterie : vanilla
- Deux derniers crans avant : vitesse/propulsion x3.5
- Deux derniers crans avant : carburant x8
- Vitesse max sous-marin joueur : 45 km/h
- Sliders F10 : Batterie, Oxygene, SuperVitesse et Torpilles de 1 a 100
- Slider Batterie : 1 = vanilla, 100 = drain electrique coupe et batterie maintenue au maximum
- Slider Oxygene : 1 = vanilla, 100 = profil environ 90 jours
- Slider SuperVitesse : 1 = vanilla, 3.5 = defaut actuel, 100 = extreme
- Slider Torpilles : 1 = vanilla, 10 = defaut actuel, 100 = extreme
- Bouton Par defaut : restaure les reglages du profil actuel
- Mega torpilles : oui
- Mega torpilles degats : x10
- Mega torpilles rayon explosion : x10
- Mega torpilles intensite explosion : x10
- Mega torpilles guidage : cible verrouillee corrigee pendant le vol
- Fiabilite parfaite torpilles : oui
- DudChance torpilles : 0
- Defaillance magnetique torpilles : 0
- Explosion magnetique prematuree torpilles : 0
- Menu en jeu : F10 pour activer/desactiver Mega Batterie, Mega Oxygene, SuperVitesse et Mega Torpilles
- DLC Type IX officiel : lignes joueur Type IXA/IXC/IXC40 incluses si le DLC est installe
- Ventilation vanilla : oui
- Patch runtime : LongSubmerged10xPatch_1_3_8, air apres chargement, plafond vitesse, propulseurs, carburant rapide, torpilles et menu

Installation :
1. Fermer UBOAT.
2. Générer avec --force --clear-cache.
3. Activer le mod dans le launcher.
4. Charger la sauvegarde ou démarrer une nouvelle carrière pour tester les changements d'air.

Notes :
- La jauge du jeu est une qualité d'air/atmosphère, pas un vrai compteur O2 détaillé.
- La lumière bleue reste vanilla et doit toujours aider en immersion silencieuse.
- La ventilation reste vanilla par défaut pour éviter les bugs vus dans les essais précédents.
- Le patch runtime recalcule l'oxygène sur les sauvegardes existantes qui gardaient l'ancien -4/min.
- Le profil air vise environ 90 jours d'immersion avec Mega Oxygene actif.
- Mega Batterie est reglable en runtime ; 1 revient vanilla, 100 coupe le drain electrique positif.
- Les sliders F10 sont persistants et s'appliquent directement en partie avec Reappliquer maintenant ou au changement de valeur.
- Les vitesses lentes et mi-vitesse restent vanilla ; seuls les deux crans rapides avant sont boostés vers 40/45 km/h.
- Les crans rapides consomment plus de carburant pour garder une autonomie logique.
- Les torpilles gardent leur vitesse/portee vanilla ; les degats, explosions, rates et le guidage verrouille sont geres en runtime.
- Le guidage mega met les tirs verrouilles en cible cartésienne dynamique et force l'impact a courte distance.
- La fiabilite parfaite met DudChance, MagneticExplosionFail, MagneticExplosionOnArm et MagneticExplosionAfterArm a 0 quand Mega Torpilles est actif.
- Couper Mega Torpilles remet les torpilles sur les valeurs vanilla, car les XLSX torpilles ne sont pas ecrases.
- La recharge diesel reste vanilla pour éviter une recharge batterie instantanée.
- Le plafond de vitesse inclut les Type IX officiels du DLC Steam quand le DLC est installe.
- Compatible sauvegarde existante après fermeture complète puis relance du jeu.
- Si un autre mod touche l'air, mets Long Submerged 10x+ après lui dans l'ordre de chargement.

Compteurs de génération :
- Lignes General Oxygen Consumption : 2
- Lignes capacité air Parameters : 0
- Lignes capacité air cellules : 0
- Lignes batterie : 3
- Lignes EnergyUsage consommation : 6
- Lignes EnergyUsage recharge : 0
- Mega Batterie : runtime F10 reglable 1-100, 100 coupe le drain electrique positif et maintient la ressource au maximum par defaut
- Menu F10 : sliders runtime 1-100 et bouton Par defaut
- SuperVitesse : runtime F10 reglable 1-100 sur les deux crans rapides avant
- Lignes vitesse sous-marin joueur : 8
- Mega torpilles : runtime F10 reglable 1-100, defaut x10, guidage cible verrouillee, aucune ligne torpille XLSX ecrasee
